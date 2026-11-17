using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Aviss.Application.Importacion;

namespace Aviss.Excel.Tests;

/// <summary>RF-23: la importación no usa la red. Se revisan los ensamblados propios (no los de NPOI).</summary>
public class DependenciasTests
{
    private static readonly Assembly[] Propios = [typeof(PerfilDairyComp).Assembly, typeof(IExcelProfile).Assembly];

    [Fact]
    public void Excel_y_Application_no_referencian_ensamblados_de_red()
    {
        foreach (var ensamblado in Propios)
        {
            var prohibidos = ensamblado.GetReferencedAssemblies()
                .Select(a => a.Name!)
                .Where(n => n.StartsWith("System.Net", StringComparison.Ordinal));

            Assert.True(!prohibidos.Any(), $"{ensamblado.GetName().Name} referencia: {string.Join(", ", prohibidos)}");
        }
    }

    [Fact]
    public void Ninguna_firma_de_Excel_ni_de_Application_usa_tipos_de_red()
    {
        foreach (var ensamblado in Propios)
            foreach (var tipo in ensamblado.GetTypes())
            {
                var miembros = tipo.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
                foreach (var tipoUsado in miembros.SelectMany(TiposDe))
                    Assert.False(
                        tipoUsado.Namespace?.StartsWith("System.Net", StringComparison.Ordinal) == true,
                        $"{tipo.FullName} usa {tipoUsado.FullName}");
            }
    }

    private static IEnumerable<Type> TiposDe(MemberInfo m) => m switch
    {
        MethodBase mb => mb.GetParameters().Select(p => p.ParameterType)
            .Concat(mb is MethodInfo mi ? [mi.ReturnType] : []),
        PropertyInfo p => [p.PropertyType],
        FieldInfo f => [f.FieldType],
        _ => []
    };

    [Fact]
    public void AddAvissExcel_registra_el_perfil_de_Dairy_Comp()
    {
        var servicios = new ServiceCollection().AddAvissExcel();

        var registro = Assert.Single(servicios, d => d.ServiceType == typeof(IExcelProfile));
        Assert.Equal(typeof(PerfilDairyComp), registro.ImplementationType);
    }
}
