using System.Globalization;
using Aviss.Application.Campo;
using Aviss.Application.Comandos;
using Aviss.Application.Comun;
using Aviss.Application.Puertos;
using Aviss.Application.Servicios;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aviss.App.ViewModels;

/// <summary>Pantalla de trabajo: el control por manga (cantidad, contador 7/10, cierre automático), el engranaje, "Finalizar manga" y "Terminar el tacto".</summary>
public sealed partial class TrabajoViewModel
{
    // ---- Control por manga: cada veterinario lo prende o lo apaga a su gusto (engranaje de arriba a la izquierda)

    private bool _cargandoPreferencias;

    /// <summary>
    /// Se lleva el control por manga en este establecimiento: contador "7/10", cantidad de la manga, cierre automático y "Finalizar
    /// manga". Apagado, se ve todo lo demás sin lo de las mangas (la app sigue usando una manga interna para guardar los datos).
    /// Es una preferencia de cada establecimiento (cada tambo trabaja a su manera): se guarda al cambiarla y se vuelve a leer al empezar. No depende del veterinario.
    /// </summary>
    [ObservableProperty]
    private bool _usaMangas = IPreferenciasDispositivo.ControlPorMangaPorDefecto;

    partial void OnUsaMangasChanged(bool value)
    {
        NotificarMeta();
        OnPropertyChanged(nameof(TextoSegundaConfirmacion));
        OnPropertyChanged(nameof(HayAdvertenciaMangaAbierta));
        OnPropertyChanged(nameof(AdvertenciaMangaAbierta));
        if (_cargandoPreferencias || _sesion.Rodeo is not { } rodeo) return;
        _preferencias.GuardarControlPorManga(rodeo.EstablecimientoId, value);
    }

    // Lee la preferencia del establecimiento en trabajo (sin volver a guardarla). Si nunca se eligió, queda apagado.
    private void CargarPreferenciasDelEstablecimiento()
    {
        if (_sesion.Rodeo is not { } rodeo) return;
        _cargandoPreferencias = true;
        try { UsaMangas = _preferencias.ObtenerControlPorManga(rodeo.EstablecimientoId) ?? IPreferenciasDispositivo.ControlPorMangaPorDefecto; }
        finally { _cargandoPreferencias = false; }
    }

    // ---- Configuración (el engranaje): una capa con las opciones del establecimiento, de un solo toque

    public bool HayConfiguracion => Cuadro == CuadroTrabajo.Configuracion;

    [RelayCommand]
    private async Task AbrirConfiguracionAsync()
    {
        if (Cuadro != CuadroTrabajo.Ninguno) return;
        await CambiarCuadroAsync(CuadroTrabajo.Configuracion);
    }

    [RelayCommand]
    private async Task CerrarConfiguracionAsync()
    {
        if (Cuadro != CuadroTrabajo.Configuracion) return;
        await CambiarCuadroAsync(CuadroTrabajo.Ninguno);
    }


    // ---- Cantidad de vacas de la manga: se pone a mano cada vez que entra una manga y el contador se muestra contra ese número

    private int? _metaValor;
    private int _metaNumeroManga;

    /// <summary>
    /// Cuántas vacas entran en la manga actual (10, 12...), o nulo si todavía no se puso. Vale solo para el número de manga en el
    /// que se puso: al pasar a la siguiente se borra y hay que ponerla de nuevo. Es una ayuda para no olvidarse de marcar a
    /// ninguna; no se guarda en la base.
    /// </summary>
    public int? MetaManga => UsaMangas && _metaValor is { } meta && _metaNumeroManga == NumeroManga ? meta : null;

    public bool HayMeta => MetaManga is not null;

    /// <summary>Las marcadas son justo las que tenían que ser (10/10).</summary>
    public bool MangaCompleta => MetaManga is { } meta && Contador == meta;

    /// <summary>Hay más marcadas que la cantidad puesta (11/10).</summary>
    public bool MangaExcedida => MetaManga is { } meta && Contador > meta;

    /// <summary>Lo que se está escribiendo en el cuadro de cantidad.</summary>
    [ObservableProperty]
    private string _textoCantidadManga = "";

    public bool HayCantidadManga => Cuadro == CuadroTrabajo.CantidadManga;

    private void NotificarMeta()
    {
        OnPropertyChanged(nameof(MetaManga));
        OnPropertyChanged(nameof(HayMeta));
        OnPropertyChanged(nameof(MangaCompleta));
        OnPropertyChanged(nameof(MangaExcedida));
        OnPropertyChanged(nameof(TextoManga));
        OnPropertyChanged(nameof(HayMetaVerificacion));
        OnPropertyChanged(nameof(TextoMetaVerificacion));
    }

    /// <summary>Toca el contador: abre el cuadro para poner (o cambiar) cuántas vacas entran en esta manga.</summary>
    [RelayCommand]
    private async Task AbrirCantidadMangaAsync()
    {
        if (!UsaMangas || Cuadro != CuadroTrabajo.Ninguno) return;
        TextoCantidadManga = MetaManga?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
        MensajeCuadro = null;
        await CambiarCuadroAsync(CuadroTrabajo.CantidadManga);
    }

    [RelayCommand]
    private async Task GuardarCantidadMangaAsync()
    {
        if (Cuadro != CuadroTrabajo.CantidadManga) return;
        if (!int.TryParse(TextoCantidadManga.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var cantidad)
            || cantidad < 1 || cantidad > 999)
        {
            MensajeCuadro = "Escribí cuántas vacas entran en la manga (de 1 a 999).";
            return;
        }

        _metaValor = cantidad;
        _metaNumeroManga = NumeroManga;
        NotificarMeta();
        MensajeCuadro = null;
        await CambiarCuadroAsync(CuadroTrabajo.Ninguno);
    }

    /// <summary>Cierra el cuadro de cantidad sin cambiar lo que había.</summary>
    [RelayCommand]
    private async Task CerrarCantidadMangaAsync()
    {
        if (Cuadro != CuadroTrabajo.CantidadManga) return;
        MensajeCuadro = null;
        await CambiarCuadroAsync(CuadroTrabajo.Ninguno);
    }

    /// <summary>Al verificar para finalizar: cuántas se esperaban contra cuántas hay marcadas ("faltan 1", "sobra 1", "completa").</summary>
    public bool HayMetaVerificacion => HayVerificacion && MetaManga is not null && ResumenManga is not null;

    public string TextoMetaVerificacion
    {
        get
        {
            if (!HayMetaVerificacion) return "";
            var esperadas = MetaManga!.Value;
            var marcadas = ResumenManga!.Contador;
            var resultado = marcadas == esperadas ? "completa"
                : marcadas < esperadas ? (esperadas - marcadas == 1 ? "falta 1" : $"faltan {esperadas - marcadas}")
                : (marcadas - esperadas == 1 ? "sobra 1" : $"sobran {marcadas - esperadas}");
            return $"Esperadas: {esperadas} · Marcadas: {marcadas} · {resultado}";
        }
    }


    // ---- Cierre de manga (RF-6, RF-7)

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayMetaVerificacion), nameof(TextoMetaVerificacion))]
    private ResumenMangaDto? _resumenManga;

    [ObservableProperty]
    private string _cantidadRealTexto = "";

    /// <summary>Resultado de "Terminar el tacto": se muestra en el resumen con "Deshacer" (RF-7).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayResumenTerminado), nameof(PuedeDeshacerTerminado), nameof(TextoResumenTerminado))]
    private ResultadoCierreDto? _resultadoTerminado;

    public bool HayResumenTerminado => Etapa == EtapaTrabajo.Terminado;
    public bool PuedeDeshacerTerminado => Etapa == EtapaTrabajo.Terminado && ResultadoTerminado?.Cierre is not null;

    /// <summary>La pregunta final de "Terminar el tacto", que termina todo el trabajo.</summary>
    public string TextoSegundaConfirmacion => "¿Estás seguro que querés terminar?";

    /// <summary>Con el control por manga prendido y una manga abierta, se advierte cuál es antes de terminar (se cierra sola).</summary>
    public bool HayAdvertenciaMangaAbierta => UsaMangas && MangaAbierta;

    public string AdvertenciaMangaAbierta => HayAdvertenciaMangaAbierta
        ? $"Hay una manga abierta ({TextoManga}). Al terminar se cierra y no se abre otra."
        : "";

    /// <summary>Resumen del final del tacto: la manga cerrada, las dos cantidades y lo procesado hoy (RF-7).</summary>
    public string TextoResumenTerminado
    {
        get
        {
            if (ResultadoTerminado is not { } r) return "";
            var avance = $"Procesadas hoy: {r.Avance.Procesadas} de {r.Avance.TotalVacas}.";
            if (r.Cierre is not { } c) return $"No había una manga abierta. {avance}";
            var cantidades = $"Aviss contó {c.CantidadAviss}" + (c.CantidadConfirmada is { } conf ? $" y se confirmaron {conf}" : "");
            // Si después se desmarcó una vaca de la manga cerrada, las dos cifras se muestran (RF-8).
            var hoy = c.ContadorActual != c.CantidadAviss ? $" (hoy: {c.ContadorActual})" : "";
            return $"Manga {c.NumeroManga} cerrada: {cantidades}{hoy}. {avance}";
        }
    }


    // ================================================================== Finalizar manga y Terminar el tacto (RF-6, RF-7)

    [RelayCommand]
    private Task FinalizarMangaAsync() => IniciarCierreAsync(PropositoCierre.Finalizar);

    [RelayCommand]
    private Task TerminarElTactoAsync() => IniciarCierreAsync(PropositoCierre.Terminar);

    private async Task IniciarCierreAsync(PropositoCierre proposito)
    {
        if (Cuadro != CuadroTrabajo.Ninguno || Etapa == EtapaTrabajo.Terminado || _sesion.Rodeo is not { } rodeo) return;
        // Sin control por manga no hay "Finalizar manga": la manga es interna y solo se cierra al terminar el tacto.
        if (!UsaMangas && proposito == PropositoCierre.Finalizar) return;
        try
        {
            MensajeError = null;
            MensajeInfo = null;
            MensajeCuadro = null;
            _proposito = proposito;
            _cantidadRealConfirmada = null;
            CantidadRealTexto = "";

            if (!UsaMangas)
            {
                // "Terminar el tacto" termina todo el trabajo: sin control por manga no se verifica la cantidad ni las
                // caravanas, solo se pide la confirmación. La manga abierta se cierra igual como "Coincide".
                ResumenManga = null;
                await CambiarCuadroAsync(CuadroTrabajo.SegundaConfirmacion);
                return;
            }

            var resumen = await EnCampoAsync(() => _campo.ObtenerResumenMangaAsync(rodeo.RodeoId));
            if (resumen.EsCorrecto)
            {
                ResumenManga = resumen.Valor;
                await CambiarCuadroAsync(CuadroTrabajo.Verificacion);
            }
            else if (resumen.Codigo == CodigoError.NoHayMangaAbierta && proposito == PropositoCierre.Terminar)
            {
                // Sin manga abierta, "Terminar el tacto" solo pide la segunda confirmación y no cierra nada (RF-7).
                ResumenManga = null;
                await CambiarCuadroAsync(CuadroTrabajo.SegundaConfirmacion);
            }
            else
            {
                MensajeError = resumen.Mensaje;   // "No hay una manga abierta: no hay nada para finalizar."
            }
        }
        catch (Exception) { MensajeError = MensajeFalla; }
    }

    /// <summary>"Coincide": la cantidad real es la que contó Aviss.</summary>
    [RelayCommand]
    private async Task CoincideAsync()
    {
        if (Cuadro != CuadroTrabajo.Verificacion) return;
        _cantidadRealConfirmada = null;
        await ContinuarCierreAsync();
    }

    /// <summary>"No coincide": pide la cantidad real.</summary>
    [RelayCommand]
    private async Task NoCoincideAsync()
    {
        if (Cuadro != CuadroTrabajo.Verificacion) return;
        MensajeCuadro = null;
        await CambiarCuadroAsync(CuadroTrabajo.CantidadReal);
    }

    /// <summary>Con la cantidad real cargada: sigue el cierre (cerrar igual, dejando la diferencia registrada).</summary>
    [RelayCommand]
    private async Task ConfirmarCantidadRealAsync()
    {
        if (Cuadro != CuadroTrabajo.CantidadReal) return;
        // Solo dígitos: un entero de cero para arriba (el servicio lo vuelve a validar).
        if (!int.TryParse(CantidadRealTexto.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var cantidad))
        {
            MensajeCuadro = MensajesCampo.Para(CodigoError.CantidadInvalida);
            return;
        }
        MensajeCuadro = null;
        _cantidadRealConfirmada = cantidad;
        await ContinuarCierreAsync();
    }

    /// <summary>"Volver a corregir": cierra el cuadro sin cerrar nada, para marcar a mano o deshacer una de más.</summary>
    [RelayCommand]
    private async Task VolverACorregirAsync()
    {
        if (Cuadro is not (CuadroTrabajo.Verificacion or CuadroTrabajo.CantidadReal)) return;
        MensajeCuadro = null;
        await CambiarCuadroAsync(CuadroTrabajo.Ninguno);
    }

    // Después de la verificación: "Finalizar" cierra ya; "Terminar" pasa a la segunda confirmación (no se guarda nada todavía).
    private async Task ContinuarCierreAsync()
    {
        if (_proposito == PropositoCierre.Terminar)
            await CambiarCuadroAsync(CuadroTrabajo.SegundaConfirmacion);
        else
            await CerrarMangaAsync();
    }


    /// <summary>
    /// Al marcar la última vaca de la cantidad puesta (10/10), la manga se cierra sola, sin verificación (lo contado coincide con
    /// la cantidad), y suena un aviso corto con una alerta chica. La ficha de esa última vaca sigue abierta para tactarla; la
    /// siguiente manga queda por empezar y sin cantidad. Se puede deshacer con el aviso de 5 segundos o desde los últimos movimientos.
    /// Si el cierre falla no se bloquea nada: se muestra el motivo y se sigue trabajando.
    /// </summary>
    private async Task CerrarMangaSiEstaCompletaAsync()
    {
        if (!MangaAbierta || MetaManga is not { } meta || Contador != meta) return;
        if (_sesion.Veterinario is not { } vet || _sesion.Rodeo is not { } rodeo) return;

        try
        {
            var numero = NumeroManga;
            var resultado = await EnCampoAsync(() => _campo.FinalizarMangaAsync(new FinalizarMangaCommand(rodeo.RodeoId, null, vet.Id)));
            if (!resultado.EsCorrecto)
            {
                MensajeInfo = resultado.Mensaje;
                return;
            }

            var texto = $"Manga {numero} terminada ({meta}/{meta})";
            _avisos.MangaCompleta();
            UltimoGuardado = texto;
            if (resultado.Valor.Cierre is { } cierre)
                PublicarAvisoDeshacer(new AvisoDeshacerDto(TipoDeshacer.Cierre, cierre.CierreId, texto));
            Avance = resultado.Valor.Avance;
            await RecalcularAsync();   // la siguiente manga queda por empezar, sin cantidad
        }
        catch (Exception) { MensajeInfo = MensajeFalla; }
    }

    private async Task CerrarMangaAsync()
    {
        if (_sesion.Veterinario is not { } vet || _sesion.Rodeo is not { } rodeo) return;
        try
        {
            var resultado = await EnCampoAsync(() => _campo.FinalizarMangaAsync(new FinalizarMangaCommand(rodeo.RodeoId, _cantidadRealConfirmada, vet.Id)));
            if (!resultado.EsCorrecto)
            {
                MensajeCuadro = resultado.Mensaje;   // se queda en el cuadro con el motivo
                return;
            }

            var cierre = resultado.Valor.Cierre;
            await CambiarCuadroAsync(CuadroTrabajo.Ninguno);
            CerrarFichaInterno();
            _avisos.Confirmacion();
            if (cierre is not null)
            {
                UltimoGuardado = $"Manga {cierre.NumeroManga} cerrada";
                PublicarAvisoDeshacer(new AvisoDeshacerDto(TipoDeshacer.Cierre, cierre.CierreId, $"Manga {cierre.NumeroManga} cerrada"));
            }
            Avance = resultado.Valor.Avance;
            await RecalcularAsync();   // la siguiente manga queda por empezar
        }
        catch (Exception) { MensajeCuadro = MensajeFalla; }
    }

    /// <summary>"Sí, terminar": solo acá, con las dos confirmaciones, se cierra la manga y se da por terminado el tacto.</summary>
    [RelayCommand]
    private async Task ConfirmarTerminarAsync()
    {
        if (Cuadro != CuadroTrabajo.SegundaConfirmacion || _sesion.Veterinario is not { } vet || _sesion.Rodeo is not { } rodeo) return;
        try
        {
            var resultado = await EnCampoAsync(() => _campo.TerminarElTactoAsync(new TerminarElTactoCommand(rodeo.RodeoId, _cantidadRealConfirmada, vet.Id)));
            if (!resultado.EsCorrecto)
            {
                MensajeCuadro = resultado.Mensaje;
                return;
            }

            await CambiarCuadroAsync(CuadroTrabajo.Ninguno);
            CerrarFichaInterno();
            QuitarAvisoDeshacer();   // el resumen ya trae su propio "Deshacer"
            ResultadoTerminado = resultado.Valor;
            Etapa = EtapaTrabajo.Terminado;
            _avisos.Confirmacion();
            await RecalcularAsync();
        }
        catch (Exception) { MensajeCuadro = MensajeFalla; }
    }

    /// <summary>"No, seguir trabajando": no se cerró nada.</summary>
    [RelayCommand]
    private async Task NoTerminarAsync()
    {
        if (Cuadro != CuadroTrabajo.SegundaConfirmacion) return;
        await CambiarCuadroAsync(CuadroTrabajo.Ninguno);
    }

    /// <summary>"Deshacer" del resumen de "Terminar el tacto": reabre la manga y se sigue trabajando (RF-9).</summary>
    [RelayCommand]
    private async Task DeshacerTerminadoAsync()
    {
        if (!PuedeDeshacerTerminado || ResultadoTerminado?.Cierre is not { } cierre) return;
        try
        {
            MensajeError = null;
            var resultado = await DeshacerAsync(TipoDeshacer.Cierre, cierre.CierreId);
            if (!resultado.EsCorrecto) { MensajeError = resultado.Mensaje; return; }

            ResultadoTerminado = null;
            Etapa = EtapaTrabajo.Esperando;
        }
        catch (Exception) { MensajeError = MensajeFalla; }
    }

    /// <summary>"Salir" del resumen: vuelve a elegir rodeo.</summary>
    [RelayCommand]
    private async Task SalirAsync()
    {
        await DetenerAsync();
        _sesion.SalirDelRodeo();
        _rodeoIniciado = null;
        await _navegacion.IrAEleccionTrabajoAsync();
    }

}
