using Aviss.App.ViewModels;
using Aviss.Application.Puertos;

namespace Aviss.Application.Tests.ViewModels;

/// <summary>Spec 008 (RF-1, RF-4, RF-5, RF-6, RF-11, RF-12, RF-13): del estado y el motivo del lector al texto, la acción y el color.</summary>
public class PresentacionBastonTests
{
    [Fact]
    public void Conectado_es_verde_sin_cartel_ni_accion()
    {
        var p = PresentacionBaston.Crear(EstadoConexionRfid.Conectado, MotivoSinBaston.Ninguno);

        Assert.Equal(EstadoBaston.Conectado, p.Estado);
        Assert.Equal("Conectado", p.Texto);
        Assert.Equal(PresentacionBaston.ColorVerde, p.ColorFondo);
        Assert.False(p.HayAviso);
        Assert.Equal(AccionBaston.Ninguna, p.Accion);
    }

    [Fact]
    public void Buscando_es_ambar_y_muestra_el_cartel()
    {
        var p = PresentacionBaston.Crear(EstadoConexionRfid.Conectando, MotivoSinBaston.Ninguno);

        Assert.Equal(EstadoBaston.Buscando, p.Estado);
        Assert.Equal("Buscando el bastón", p.Texto);
        Assert.Equal(PresentacionBaston.ColorAmbar, p.ColorFondo);
        Assert.True(p.HayAviso);
    }

    [Theory]
    [InlineData(MotivoSinBaston.NoElegido, "Elegí el bastón", AccionBaston.ElegirBaston)]
    [InlineData(MotivoSinBaston.NoEmparejado, "El bastón ya no está emparejado", AccionBaston.ElegirBaston)]
    [InlineData(MotivoSinBaston.SinPermiso, "Falta el permiso de Bluetooth", AccionBaston.PedirPermiso)]
    [InlineData(MotivoSinBaston.BluetoothApagado, "Prendé el Bluetooth de la tablet", AccionBaston.AbrirAjustesBluetooth)]
    [InlineData(MotivoSinBaston.NoResponde, "El bastón no responde: prendelo o acercalo", AccionBaston.Reconectar)]
    [InlineData(MotivoSinBaston.SinBluetooth, "Esta tablet no tiene Bluetooth", AccionBaston.Ninguna)]
    public void Sin_baston_dice_el_motivo_y_la_accion_que_corresponde(MotivoSinBaston motivo, string detalle, AccionBaston accion)
    {
        var p = PresentacionBaston.Crear(EstadoConexionRfid.Desconectado, motivo);

        Assert.Equal(EstadoBaston.SinBaston, p.Estado);
        Assert.Equal("Sin bastón", p.Texto);
        Assert.Equal(detalle, p.Detalle);
        Assert.Equal(accion, p.Accion);
        Assert.Equal(PresentacionBaston.ColorRojo, p.ColorFondo);
        Assert.True(p.HayAviso);
    }

    [Fact]
    public void Sin_motivo_definido_se_ofrece_reconectar()
    {
        // Pasa con el lector recién cortado (pausa del ciclo de vida) o con un lector que no tiene motivos.
        var p = PresentacionBaston.Crear(EstadoConexionRfid.Desconectado, MotivoSinBaston.Ninguno);

        Assert.Equal(EstadoBaston.SinBaston, p.Estado);
        Assert.Equal(AccionBaston.Reconectar, p.Accion);
    }

    [Fact]
    public void Tocar_para_reconectar_dice_exactamente_eso_y_los_demas_dicen_que_hacer()
    {
        Assert.Equal("Tocá para reconectar", PresentacionBaston.Crear(EstadoConexionRfid.Desconectado, MotivoSinBaston.NoResponde).TextoAccion);
        Assert.Contains("Dispositivos cercanos", PresentacionBaston.Crear(EstadoConexionRfid.Desconectado, MotivoSinBaston.SinPermiso).TextoAccion);
        Assert.Equal("", PresentacionBaston.Crear(EstadoConexionRfid.Desconectado, MotivoSinBaston.SinBluetooth).TextoAccion);
    }

    [Fact]
    public void Cada_estado_tiene_un_color_distinto_ademas_del_texto()
    {
        var verde = PresentacionBaston.Crear(EstadoConexionRfid.Conectado, MotivoSinBaston.Ninguno).ColorFondo;
        var ambar = PresentacionBaston.Crear(EstadoConexionRfid.Conectando, MotivoSinBaston.Ninguno).ColorFondo;
        var rojo = PresentacionBaston.Crear(EstadoConexionRfid.Desconectado, MotivoSinBaston.NoResponde).ColorFondo;

        Assert.Equal(3, new[] { verde, ambar, rojo }.Distinct().Count());
    }
}
