using System.Text;
using Aviss.Rfid.Diagnostico;

namespace Aviss.Rfid.Tests.Diagnostico;

public class DecodificadorTramaTests
{
    private static byte[] B(string texto) => Encoding.Latin1.GetBytes(texto);

    [Fact]
    public void Devuelve_hex_texto_y_longitud()
    {
        var t = DecodificadorTrama.Decodificar(B("AB1"));
        Assert.Equal("41 42 31", t.Hex);
        Assert.Equal("AB1", t.Texto);
        Assert.Equal(3, t.Longitud);
        Assert.Empty(t.Separadores);
    }

    [Fact]
    public void Marca_los_caracteres_no_imprimibles()
    {
        var t = DecodificadorTrama.Decodificar([0x31, 0x0D, 0x0A, 0x00, 0x1B, 0xFF]);
        Assert.Equal("1<CR><LF><NUL><0x1B><0xFF>", t.Texto);
    }

    [Fact]
    public void Detecta_CRLF_como_par_y_no_como_CR_y_LF_sueltos()
    {
        var t = DecodificadorTrama.Decodificar(B("982000123456789\r\n"));
        Assert.Equal(1, t.Cantidad("CRLF"));
        Assert.Equal(0, t.Cantidad("CR"));
        Assert.Equal(0, t.Cantidad("LF"));
    }

    [Fact]
    public void Detecta_CR_solo_y_LF_solo()
    {
        Assert.Equal(1, DecodificadorTrama.Decodificar(B("1\r")).Cantidad("CR"));
        Assert.Equal(2, DecodificadorTrama.Decodificar(B("1\n2\n")).Cantidad("LF"));
    }

    [Fact]
    public void Detecta_otros_separadores()
    {
        var t = DecodificadorTrama.Decodificar([0x02, (byte)'1', (byte)',', (byte)'2', (byte)';', 0x09, (byte)' ', 0x03, 0x00]);
        Assert.Equal(1, t.Cantidad("STX"));
        Assert.Equal(1, t.Cantidad("ETX"));
        Assert.Equal(1, t.Cantidad("COMA"));
        Assert.Equal(1, t.Cantidad("PUNTO_Y_COMA"));
        Assert.Equal(1, t.Cantidad("TAB"));
        Assert.Equal(1, t.Cantidad("ESPACIO"));
        Assert.Equal(1, t.Cantidad("NUL"));
    }

    [Fact]
    public void Una_trama_vacia_no_rompe()
    {
        var t = DecodificadorTrama.Decodificar([]);
        Assert.Equal("", t.Hex);
        Assert.Equal("", t.Texto);
        Assert.Equal(0, t.Longitud);
    }
}

public class AcumuladorLineasTests
{
    private static byte[] B(string texto) => Encoding.Latin1.GetBytes(texto);
    private static string[] Textos(IEnumerable<byte[]> l) => l.Select(x => Encoding.Latin1.GetString(x)).ToArray();

    [Theory]
    [InlineData("111\r222\r")]
    [InlineData("111\n222\n")]
    [InlineData("111\r\n222\r\n")]
    public void Separa_lineas_con_CR_LF_o_CRLF(string entrada)
    {
        var a = new AcumuladorLineas();
        Assert.Equal(["111", "222"], Textos(a.Agregar(B(entrada))));
        Assert.Empty(a.Pendiente);
    }

    [Fact]
    public void Junta_una_trama_partida_en_varios_paquetes()
    {
        var a = new AcumuladorLineas();
        Assert.Empty(a.Agregar(B("9820001")));
        Assert.Empty(a.Agregar(B("23456")));
        Assert.Equal(["982000123456789"], Textos(a.Agregar(B("789\r\n"))));
    }

    [Fact]
    public void Un_CRLF_partido_entre_paquetes_no_genera_una_linea_vacia()
    {
        var a = new AcumuladorLineas();
        Assert.Equal(["111"], Textos(a.Agregar(B("111\r"))));
        Assert.Empty(a.Agregar(B("\n")));
        Assert.Equal(["222"], Textos(a.Agregar(B("222\r\n"))));
    }

    [Fact]
    public void Ignora_las_lineas_vacias()
    {
        var a = new AcumuladorLineas();
        Assert.Equal(["1", "2"], Textos(a.Agregar(B("\n\n1\n\r\n2\r"))));
    }

    [Fact]
    public void Lo_que_no_tiene_terminador_queda_pendiente_y_se_puede_cerrar()
    {
        var a = new AcumuladorLineas();
        Assert.Empty(a.Agregar(B("123")));
        Assert.Equal("123", Encoding.Latin1.GetString(a.Pendiente));
        Assert.Equal("123", Encoding.Latin1.GetString(a.CerrarPendiente()!));
        Assert.Null(a.CerrarPendiente());
    }

    [Fact]
    public void Bytes_basura_sin_terminador_no_hacen_crecer_el_buffer_sin_limite()
    {
        var a = new AcumuladorLineas(maximoBytes: 8);
        var lineas = a.Agregar(Enumerable.Repeat((byte)0xAA, 20).ToArray());
        Assert.Equal(2, lineas.Count);   // dos cortes de 8 bytes; quedan 4 pendientes
        Assert.All(lineas, l => Assert.Equal(8, l.Length));
        Assert.Equal(4, a.Pendiente.Length);
    }
}

public class ParserCandidatosTests
{
    private static IReadOnlyList<CandidatoCaravana> Buscar(string texto, OpcionesParserCaravana? o = null)
        => ParserCandidatosCaravana.Buscar(texto, o ?? new OpcionesParserCaravana());

    [Fact]
    public void Encuentra_un_codigo_de_15_digitos()
    {
        var c = Assert.Single(Buscar("982000123456789"));
        Assert.Equal("982000123456789", c.Digitos);
        Assert.Equal(TipoCandidato.Digitos, c.Tipo);
        Assert.Equal("982", c.CodigoPais);
        Assert.Equal("000123456789", c.NumeroNacional);
    }

    [Fact]
    public void Lo_encuentra_dentro_de_texto_con_otros_campos()
    {
        var c = Assert.Single(Buscar("ID:982000123456789 12:30"));
        Assert.Equal("982000123456789", c.Digitos);
        Assert.Equal(3, c.Posicion);
    }

    [Fact]
    public void Acepta_guiones_y_los_saca_de_los_digitos()
    {
        var c = Assert.Single(Buscar("982-000123456789"));
        Assert.Equal("982000123456789", c.Digitos);
        Assert.Equal(TipoCandidato.ConGuiones, c.Tipo);
        Assert.Equal("982-000123456789", c.Texto);
    }

    [Fact]
    public void Sin_guiones_permitidos_no_lo_considera()
    {
        Assert.Empty(Buscar("982-000123456789", new OpcionesParserCaravana { PermitirGuiones = false }));
    }

    [Fact]
    public void No_toma_numeros_de_otra_longitud()
    {
        Assert.Empty(Buscar("98200012345678"));     // 14
        Assert.Empty(Buscar("9820001234567890"));   // 16
    }

    [Fact]
    public void Las_longitudes_son_configurables()
    {
        var o = new OpcionesParserCaravana { LongitudesDigitos = [10] };
        Assert.Single(Buscar("0123456789", o));
        Assert.Empty(Buscar("982000123456789", o));
    }

    [Fact]
    public void Los_numeros_cortos_son_opcionales()
    {
        Assert.Empty(Buscar("1234"));
        var c = Assert.Single(Buscar("1234", new OpcionesParserCaravana { MinimoDigitosCorto = 3 }));
        Assert.Equal(TipoCandidato.NumeroCorto, c.Tipo);
    }

    [Fact]
    public void Encuentra_varios_en_una_misma_trama()
    {
        Assert.Equal(2, Buscar("982000123456789,982000123456790").Count);
    }

    [Fact]
    public void Texto_sin_numeros_no_da_candidatos()
    {
        Assert.Empty(Buscar(""));
        Assert.Empty(Buscar("<STX>OK<ETX>"));
    }
}

public class SesionDiagnosticoTests
{
    private sealed class RegistroEnMemoria : IRegistroDiagnostico
    {
        public List<string> Lineas { get; } = [];
        public void Escribir(string linea) => Lineas.Add(linea);
    }

    private static (SesionDiagnostico Sesion, RegistroEnMemoria Registro) Crear()
    {
        var r = new RegistroEnMemoria();
        var hora = new DateTime(2026, 11, 24, 10, 0, 0, DateTimeKind.Utc);
        return (new SesionDiagnostico(r, () => hora, new OpcionesParserCaravana()), r);
    }

    [Fact]
    public void Cada_paquete_cuenta_y_se_registra_con_hex_texto_y_longitud()
    {
        var (s, r) = Crear();
        var e = s.RecibirBytes("SPP", Encoding.Latin1.GetBytes("982000123456789\r\n"));
        Assert.Equal(1, s.Paquetes);
        Assert.Equal(1, s.Lineas);
        Assert.Equal(17, e.Longitud);
        Assert.Contains(r.Lineas, l => l.Contains("PAQUETE") && l.Contains("39 38 32") && l.Contains("982000123456789<CR><LF>"));
        Assert.Contains(r.Lineas, l => l.Contains("LINEA") && l.Contains("candidato=982000123456789"));
    }

    [Fact]
    public void Una_trama_partida_cuenta_dos_paquetes_y_una_linea()
    {
        var (s, _) = Crear();
        s.RecibirBytes("SPP", Encoding.Latin1.GetBytes("98200012"));
        s.RecibirBytes("SPP", Encoding.Latin1.GetBytes("3456789\r"));
        Assert.Equal(2, s.Paquetes);
        Assert.Equal(1, s.Lineas);
    }

    [Fact]
    public void Las_fuentes_no_se_mezclan()
    {
        var (s, _) = Crear();
        s.RecibirBytes("SPP", Encoding.Latin1.GetBytes("111"));
        s.RecibirBytes("BLE", Encoding.Latin1.GetBytes("222\r"));
        Assert.Equal(1, s.Lineas);   // el "111" de SPP sigue pendiente, no se pegó con el "222"
    }

    [Fact]
    public void La_anotacion_queda_ligada_a_la_ultima_lectura()
    {
        var (s, r) = Crear();
        s.RecibirBytes("SPP", Encoding.Latin1.GetBytes("982000123456789\r\n"));
        s.Anotar("4521");
        var nota = Assert.Single(r.Lineas, l => l.Contains("ANOTACION"));
        Assert.Contains("caravana_impresa=4521", nota);
        Assert.Contains("lectura=1", nota);
    }

    [Fact]
    public void Anotar_sin_lecturas_se_registra_sin_referencia()
    {
        var (s, r) = Crear();
        s.Anotar("77");
        Assert.Contains(r.Lineas, l => l.Contains("ANOTACION") && l.Contains("lectura=ninguna"));
    }

    [Fact]
    public void Limpiar_pone_los_contadores_en_cero_pero_no_borra_el_registro()
    {
        var (s, r) = Crear();
        s.RecibirBytes("SPP", Encoding.Latin1.GetBytes("1\r"));
        var antes = r.Lineas.Count;
        s.Limpiar();
        Assert.Equal(0, s.Paquetes);
        Assert.Equal(0, s.Lineas);
        Assert.True(r.Lineas.Count >= antes);
    }

    [Fact]
    public void El_teclado_registra_cada_linea()
    {
        var (s, r) = Crear();
        var e = s.RecibirLineaTeclado("982000123456789");
        Assert.Equal(1, s.Lineas);
        Assert.Contains(r.Lineas, l => l.Contains("TECLADO") && l.Contains("candidato=982000123456789"));
        Assert.Equal(15, e.Longitud);
    }

    [Fact]
    public void Un_error_o_un_aviso_se_registra_como_nota()
    {
        var (s, r) = Crear();
        s.Nota("estado: error (timeout)");
        Assert.Contains(r.Lineas, l => l.Contains("NOTA") && l.Contains("timeout"));
    }
}

public class AcumuladorTecladoTests
{
    [Fact]
    public void Extrae_las_lineas_completas_y_deja_el_resto()
    {
        var (lineas, resto) = AcumuladorTeclado.Procesar("111\n222\n33");
        Assert.Equal(["111", "222"], lineas);
        Assert.Equal("33", resto);
    }

    [Fact]
    public void Acepta_CR_y_CRLF_y_descarta_vacias()
    {
        var (lineas, resto) = AcumuladorTeclado.Procesar("1\r\n\r2\r");
        Assert.Equal(["1", "2"], lineas);
        Assert.Equal("", resto);
    }

    [Fact]
    public void Sin_terminador_no_hay_lineas()
    {
        var (lineas, resto) = AcumuladorTeclado.Procesar("abc");
        Assert.Empty(lineas);
        Assert.Equal("abc", resto);
    }
}

public class RegistroArchivoTests : IDisposable
{
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "aviss-reg-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_carpeta, true); } catch (IOException) { }
    }

    [Fact]
    public void Crea_un_archivo_por_sesion_con_la_fecha_en_el_nombre_y_escribe_al_instante()
    {
        var hora = new DateTime(2026, 11, 24, 10, 5, 9, DateTimeKind.Utc);
        using var r = new RegistroArchivo(_carpeta, () => hora);
        Assert.Equal("diagnostico-baston-20261124-100509.txt", Path.GetFileName(r.Ruta));

        r.Escribir("linea uno");
        // Sin cerrar: ya está en disco (si la tablet se apaga no se pierde).
        using var lectura = new FileStream(r.Ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(lectura);
        Assert.Contains("linea uno", sr.ReadToEnd());
    }

    [Fact]
    public void Dos_sesiones_en_el_mismo_segundo_no_se_pisan()
    {
        var hora = new DateTime(2026, 11, 24, 10, 5, 9, DateTimeKind.Utc);
        using var a = new RegistroArchivo(_carpeta, () => hora);
        using var b = new RegistroArchivo(_carpeta, () => hora);
        Assert.NotEqual(a.Ruta, b.Ruta);
    }
}
