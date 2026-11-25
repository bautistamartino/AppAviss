using System.Text;

namespace Aviss.Rfid.Tests;

/// <summary>RF-18 y RF-20. Todos los EID son inventados con prefijo 999 (nunca los reales del bastón).</summary>
public class InterpreteLineaBastonTests
{
    private static byte[] B(string texto) => Encoding.Latin1.GetBytes(texto);

    [Theory]
    [InlineData("999 000458467499")]
    [InlineData("999000458467499")]
    [InlineData("  999 000458467499  ")]
    public void Devuelve_los_15_digitos_sin_espacios(string linea)
    {
        Assert.Equal("999000458467499", InterpreteLineaBaston.Interpretar(B(linea)));
    }

    [Fact]
    public void Acepta_la_trama_byte_a_byte_sin_el_terminador()
    {
        // 39 39 39 20 30 30 30 34 35 38 34 36 37 34 39 39 (el CRLF lo saca el acumulador antes).
        byte[] trama = [0x39, 0x39, 0x39, 0x20, 0x30, 0x30, 0x30, 0x34, 0x35, 0x38, 0x34, 0x36, 0x37, 0x34, 0x39, 0x39];
        Assert.Equal("999000458467499", InterpreteLineaBaston.Interpretar(trama));
    }

    [Theory]
    [InlineData("99900045846749")]        // 14 dígitos
    [InlineData("9990004584674999")]      // 16 dígitos
    [InlineData("999 00045846749A")]      // una letra
    [InlineData("ABCDEFGHIJKLMNO")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("999-000458467499")]
    public void Lo_que_no_es_un_EID_es_basura(string linea)
    {
        Assert.Null(InterpreteLineaBaston.Interpretar(B(linea)));
    }

    [Fact]
    public void Bytes_altos_y_NUL_son_basura()
    {
        byte[] conAlto = [.. B("999 00045846749"), 0xE9];
        byte[] conNul = [.. B("999 00045846749"), 0x00];
        Assert.Null(InterpreteLineaBaston.Interpretar(conAlto));
        Assert.Null(InterpreteLineaBaston.Interpretar(conNul));
        Assert.Null(InterpreteLineaBaston.Interpretar([]));
    }

    [Fact]
    public void Dígitos_no_ASCII_no_cuentan()
    {
        // Dígito árabe-índico: es "dígito" para Unicode pero no para el bastón.
        Assert.Null(InterpreteLineaBaston.Interpretar(Encoding.UTF8.GetBytes("999 00045846749٥")));
    }
}
