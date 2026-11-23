using System.Collections.ObjectModel;
using System.Globalization;
using Aviss.Application.Campo;
using Aviss.Application.Comandos;
using Aviss.Application.Comun;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aviss.App.ViewModels;

/// <summary>
/// Una vaca en espera lista para mostrar (spec 004, RF-2): la caravana, su CVET, la hora en que se puso en espera y hace cuánto.
/// El tiempo es solo informativo: no hay temporizador ni vencimiento. Se calcula con la hora de "ahora" que le pasa el ViewModel
/// al cargar (y se vuelve a calcular en cada refresco de 60 segundos).
/// </summary>
public sealed class EsperaItem
{
    public EsperaItem(EsperaDto dto, DateTime ahoraUtc)
    {
        Dto = dto;
        var esperado = ahoraUtc - dto.FechaHoraUtc;
        Tiempo = DescribirTiempo(esperado < TimeSpan.Zero ? TimeSpan.Zero : esperado);
    }

    public EsperaDto Dto { get; }

    public Guid AnimalId => Dto.AnimalId;
    public string Caravana => Dto.Caravana;
    public string Cvet => Dto.Cvet;

    /// <summary>Hora local en que se puso en espera.</summary>
    public string Hora => Dto.FechaHoraUtc.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>"3 min", "1 h 15 min" o "2 d": hace cuánto espera.</summary>
    public string Tiempo { get; }

    /// <summary>Lo que dice el botón de la botonera: "7712 · 3 min".</summary>
    public string TextoBotonera => $"{Caravana} · {Tiempo}";

    /// <summary>
    /// Lo que dice cada fila de la lista completa (RF-2): dos líneas, "7712 · CVET 3" y "desde 10:42 · hace 3 min".
    /// Si la vaca no tiene CVET se omite ese dato.
    /// </summary>
    public string TextoLista =>
        (string.IsNullOrWhiteSpace(Cvet) ? Caravana : $"{Caravana} · CVET {Cvet}")
        + $"\ndesde {Hora} · hace {Tiempo}";

    private static string DescribirTiempo(TimeSpan t)
    {
        if (t.TotalMinutes < 1) return "<1 min";
        if (t.TotalHours < 1) return $"{(int)t.TotalMinutes} min";
        if (t.TotalDays < 1) return $"{(int)t.TotalHours} h {t.Minutes} min";
        return $"{(int)t.TotalDays} d";
    }
}

/// <summary>
/// Pantalla de trabajo: En Espera y Rechazo (spec 004). Poner una vaca en espera, tener a mano las que esperan (botonera "+N" y
/// lista), abrirlas, y el evento fijo "Rechazo". El servicio resuelve la espera en la misma transacción que el registro: acá solo
/// se vuelve a leer la lista.
/// </summary>
public sealed partial class TrabajoViewModel
{
    /// <summary>Cuántas vacas entran en la botonera; con más aparece "+N" (RF-2).</summary>
    public const int MaximoEnBotonera = 4;

    /// <summary>El evento fijo "Rechazo" de la lista de eventos: no es un estado del catálogo (decisión 1 del plan).</summary>
    public static readonly EventoCampoDto EventoRechazo = ArmadorListaEventos.EventoRechazo;

    // ---- Las esperas del rodeo (RF-2)

    /// <summary>Todas las vacas en espera, de la más antigua a la más reciente (la lista completa).</summary>
    public ObservableCollection<EsperaItem> Esperas { get; } = [];

    /// <summary>Las primeras <see cref="MaximoEnBotonera"/>: la botonera de acceso directo.</summary>
    public ObservableCollection<EsperaItem> EsperasBotonera { get; } = [];

    public bool HayEsperas => Esperas.Count > 0;
    public bool HayMasEsperas => Esperas.Count > MaximoEnBotonera;

    /// <summary>El botón "+N" con las que no entran en la botonera.</summary>
    public string TextoMasEsperas => HayMasEsperas ? $"+{Esperas.Count - MaximoEnBotonera}" : "";

    /// <summary>Aviso de "Terminar el tacto" (RF-10): no impide terminar ni saca a nadie de la espera.</summary>
    public bool HayEsperaPendiente => Esperas.Count > 0;

    public string TextoEsperaPendiente => Esperas.Count switch
    {
        0 => "",
        1 => "Hay 1 vaca en espera",
        var n => $"Hay {n} vacas en espera"
    };

    /// <summary>Vuelve a leer las esperas del rodeo (al iniciar, en el refresco, tras poner, resolver o saltear una).</summary>
    public async Task CargarEsperasAsync()
    {
        if (_sesion.Rodeo is not { } rodeo) return;
        try
        {
            var lista = await EnCampoAsync(() => _campo.ListarEsperasAsync(rodeo.RodeoId));
            var ahora = _reloj?.UtcAhora ?? DateTime.UtcNow;
            Esperas.Clear();
            EsperasBotonera.Clear();
            foreach (var dto in lista)
            {
                var item = new EsperaItem(dto, ahora);
                Esperas.Add(item);
                if (EsperasBotonera.Count < MaximoEnBotonera) EsperasBotonera.Add(item);
            }
            OnPropertyChanged(nameof(HayEsperas));
            OnPropertyChanged(nameof(HayMasEsperas));
            OnPropertyChanged(nameof(TextoMasEsperas));
            OnPropertyChanged(nameof(HayEsperaPendiente));
            OnPropertyChanged(nameof(TextoEsperaPendiente));
        }
        catch (Exception)
        {
            // Una lectura de la botonera que falla no debe molestar: la próxima la reintenta.
        }
    }

    // ---- Poner en espera (RF-1)

    /// <summary>La ficha abierta es de una vaca que entra en tacto, sin cuadros encima y que todavía no está en espera: se ve el botón "En espera".</summary>
    public bool PuedePonerEnEspera => Ficha is { EntraEnTacto: true, EnEspera: false } && Etapa == EtapaTrabajo.Ficha && Cuadro == CuadroTrabajo.Ninguno;

    /// <summary>La ficha abierta viene de una espera: se ve el cartel "en espera" y Saltar saltea la espera (RF-3).</summary>
    public bool EsEsperaAbierta => HayFicha && Ficha!.EnEspera;

    /// <summary>"En espera": guarda la espera, cierra la ficha sin guardar ningún tacto y descarta el evento y la observación.</summary>
    [RelayCommand]
    private async Task PonerEnEsperaAsync()
    {
        if (!PuedePonerEnEspera || Ficha is not { } ficha || _sesion.Veterinario is not { } vet) return;
        try
        {
            MensajeError = null;
            var resultado = await EnCampoAsync(() => _campo.PonerEnEsperaAsync(new PonerEnEsperaCommand(ficha.AnimalId, vet.Id)));
            if (!resultado.EsCorrecto)
            {
                MensajeError = resultado.Mensaje;   // por ejemplo, ya estaba en espera: la ficha sigue abierta
                return;
            }

            _avisos.Confirmacion();
            CerrarFichaInterno();
            UltimoGuardado = $"{ficha.Caravana}: en espera";
            await CargarEsperasAsync();
        }
        catch (Exception) { MensajeError = MensajeFalla; }
    }

    // ---- Abrir una espera (RF-3)

    /// <summary>
    /// Abre la ficha de una vaca en espera (desde la botonera o la lista): no la marca ni suma al conteo. Si ya no está en espera
    /// (por ejemplo, la resolvió otra tablet), no abre nada y refresca la botonera.
    /// </summary>
    [RelayCommand]
    private async Task AbrirEsperaAsync(EsperaItem? espera)
    {
        if (espera is null || Etapa == EtapaTrabajo.Terminado || Cuadro is not (CuadroTrabajo.Ninguno or CuadroTrabajo.ListaEspera)) return;
        try
        {
            MensajeError = null;
            var ficha = await EnCampoAsync(() => _campo.ObtenerFichaAsync(espera.AnimalId));
            if (!ficha.EsCorrecto) { MensajeError = ficha.Mensaje; return; }

            if (!ficha.Valor.EnEspera)
            {
                MensajeError = "Esa vaca ya no está en espera.";
                if (Cuadro == CuadroTrabajo.ListaEspera) await CambiarCuadroAsync(CuadroTrabajo.Ninguno);
                await CargarEsperasAsync();
                return;
            }

            // Primero se cierra la lista (que procesa una lectura pendiente, si la hubo) y después se muestra la ficha: lo último que
            // tocó el veterinario es lo que queda en pantalla.
            if (Cuadro == CuadroTrabajo.ListaEspera) await CambiarCuadroAsync(CuadroTrabajo.Ninguno);
            MostrarFicha(ficha.Valor);
        }
        catch (Exception) { MensajeError = MensajeFalla; }
    }

    // ---- La lista de espera completa (RF-2, RF-15)

    public bool HayListaEspera => Cuadro == CuadroTrabajo.ListaEspera;

    /// <summary>El botón "+N": abre la lista completa. Es un cuadro que frena las lecturas (se guarda la última).</summary>
    [RelayCommand]
    private async Task AbrirListaEsperaAsync()
    {
        if (Cuadro != CuadroTrabajo.Ninguno || Etapa == EtapaTrabajo.Terminado) return;
        MensajeError = null;
        await CambiarCuadroAsync(CuadroTrabajo.ListaEspera);
    }

    [RelayCommand]
    private async Task CerrarListaEsperaAsync()
    {
        if (Cuadro != CuadroTrabajo.ListaEspera) return;
        await CambiarCuadroAsync(CuadroTrabajo.Ninguno);
    }

    // ---- Saltar (RF-3): en una ficha de espera la saltea; en una normal solo cierra

    /// <summary>"Saltar": cierra la ficha. Si venía de una espera, la deja salteada (sin guardar ningún registro).</summary>
    [RelayCommand]
    private async Task CerrarFichaAsync()
    {
        if (Ficha is not { EnEspera: true } ficha || Etapa != EtapaTrabajo.Ficha)
        {
            CerrarFichaInterno();   // ficha normal: solo se cierra (sin esperar nada, así el toque es inmediato)
            return;
        }

        if (_sesion.Veterinario is not { } vet) return;
        try
        {
            MensajeError = null;
            var resultado = await EnCampoAsync(() => _campo.SaltearEsperaAsync(new SaltearEsperaCommand(ficha.AnimalId, vet.Id)));
            if (!resultado.EsCorrecto)
            {
                MensajeError = resultado.Mensaje;   // la ficha sigue abierta
                return;
            }

            CerrarFichaInterno();
            UltimoGuardado = $"{ficha.Caravana}: espera salteada";
            await CargarEsperasAsync();
        }
        catch (Exception) { MensajeError = MensajeFalla; }
    }
}
