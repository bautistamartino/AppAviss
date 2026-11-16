using Aviss.Domain.Entidades;
using Aviss.Domain.Enums;
using Aviss.Domain.Reglas;

namespace Aviss.Domain.Tests;

public class AnimalTests
{
    private static readonly DateTime Ahora = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Rodeo = Guid.NewGuid();

    private static Animal AltaEnCampo(string caravana = "123") =>
        Animal.CrearAltaEnCampo(Rodeo, Caravana.Crear(caravana), null, null, Ahora);

    private static Animal Importado(string caravana = "123") =>
        Animal.CrearImportado(Rodeo, Caravana.Crear(caravana), "982000111", Guid.NewGuid(), "obs", "{\"ID\":\"123\"}", Ahora);

    [Fact]
    public void Alta_en_campo_minima_deja_estado_y_fecha_vacios()
    {
        var a = AltaEnCampo("0123");
        Assert.NotEqual(Guid.Empty, a.Id);
        Assert.Equal(Rodeo, a.RodeoId);
        Assert.Equal("0123", a.NumeroCaravana);
        Assert.Equal(OrigenAnimal.AltaEnCampo, a.Origen);
        Assert.Null(a.IdElectronico);
        Assert.Null(a.EstadoReproductivoId);
        Assert.Null(a.FechaUltimoTactoUtc);
        Assert.Null(a.Observaciones);
        Assert.Null(a.DatosOriginalesJson);
        Assert.False(a.Anulado);
        Assert.Null(a.AnuladoEnUtc);
        Assert.Equal(Ahora, a.CreadoEnUtc);
        Assert.Equal(Ahora, a.ActualizadoEnUtc);
    }

    [Fact]
    public void Alta_en_campo_guarda_id_electronico_sin_espacios()
    {
        var a = Animal.CrearAltaEnCampo(Rodeo, Caravana.Crear("1"), "982 000 1", "vaca flaca", Ahora);
        Assert.Equal("9820001", a.IdElectronico);
        Assert.Equal("vaca flaca", a.Observaciones);
    }

    [Fact]
    public void Importado_guarda_columnas_originales_y_sin_fecha_de_ultimo_tacto()
    {
        var a = Importado();
        Assert.Equal(OrigenAnimal.Importado, a.Origen);
        Assert.Equal("{\"ID\":\"123\"}", a.DatosOriginalesJson);
        Assert.NotNull(a.EstadoReproductivoId);
        Assert.Null(a.FechaUltimoTactoUtc);
    }

    [Fact]
    public void Importado_exige_los_datos_originales()
    {
        Assert.Throws<DominioException>(() =>
            Animal.CrearImportado(Rodeo, Caravana.Crear("1"), null, null, null, " ", Ahora));
    }

    [Fact]
    public void Dos_animales_tienen_ids_distintos()
    {
        Assert.NotEqual(AltaEnCampo("1").Id, AltaEnCampo("2").Id);
    }

    [Fact]
    public void Rechaza_rodeo_vacio_y_fechas_no_utc()
    {
        Assert.Throws<DominioException>(() =>
            Animal.CrearAltaEnCampo(Guid.Empty, Caravana.Crear("1"), null, null, Ahora));
        Assert.Throws<DominioException>(() =>
            Animal.CrearAltaEnCampo(Rodeo, Caravana.Crear("1"), null, null, DateTime.Now));
    }

    [Fact]
    public void AplicarTacto_actualiza_estado_y_fecha_de_ultimo_tacto()
    {
        var a = AltaEnCampo();
        var estado = Guid.NewGuid();
        var fecha = Ahora.AddMinutes(5);
        a.AplicarTacto(estado, fecha, Ahora.AddMinutes(5));
        Assert.Equal(estado, a.EstadoReproductivoId);
        Assert.Equal(fecha, a.FechaUltimoTactoUtc);
        Assert.Equal(Ahora.AddMinutes(5), a.ActualizadoEnUtc);
    }

    [Fact]
    public void RevertirTacto_puede_dejar_estado_y_fecha_vacios()
    {
        var a = AltaEnCampo();
        a.AplicarTacto(Guid.NewGuid(), Ahora, Ahora);
        a.RevertirTacto(null, null, Ahora.AddMinutes(1));
        Assert.Null(a.EstadoReproductivoId);
        Assert.Null(a.FechaUltimoTactoUtc);
    }

    [Fact]
    public void Anular_alta_en_campo_marca_anulado_y_fecha()
    {
        var a = AltaEnCampo();
        a.Anular(Ahora.AddMinutes(2));
        Assert.True(a.Anulado);
        Assert.Equal(Ahora.AddMinutes(2), a.AnuladoEnUtc);
        Assert.Equal("123", a.NumeroCaravana); // la caravana no se toca: la libera el índice parcial
    }

    [Fact]
    public void Un_animal_importado_no_se_anula()
    {
        Assert.Throws<DominioException>(() => Importado().Anular(Ahora));
    }

    [Fact]
    public void No_se_anula_dos_veces()
    {
        var a = AltaEnCampo();
        a.Anular(Ahora);
        Assert.Throws<DominioException>(() => a.Anular(Ahora));
    }

    [Fact]
    public void Animal_anulado_no_admite_ediciones_ni_tactos()
    {
        var a = AltaEnCampo();
        a.Anular(Ahora);
        Assert.Throws<DominioException>(() => a.CambiarObservaciones("x", Ahora));
        Assert.Throws<DominioException>(() => a.CambiarIdElectronico("1", Ahora));
        Assert.Throws<DominioException>(() => a.CambiarCaravana(Caravana.Crear("9"), Ahora));
        Assert.Throws<DominioException>(() => a.AplicarTacto(Guid.NewGuid(), Ahora, Ahora));
    }

    [Fact]
    public void Edita_observaciones_e_id_electronico()
    {
        var a = Importado();
        a.CambiarObservaciones("nueva", Ahora.AddMinutes(1));
        a.CambiarIdElectronico(null, Ahora.AddMinutes(2));
        Assert.Equal("nueva", a.Observaciones);
        Assert.Null(a.IdElectronico);
        Assert.Equal(Ahora.AddMinutes(2), a.ActualizadoEnUtc);
    }

    [Fact]
    public void Caravana_solo_cambia_en_altas_en_campo()
    {
        var campo = AltaEnCampo("1");
        campo.CambiarCaravana(Caravana.Crear("2"), Ahora);
        Assert.Equal("2", campo.NumeroCaravana);

        Assert.Throws<DominioException>(() => Importado("5").CambiarCaravana(Caravana.Crear("6"), Ahora));
    }
}
