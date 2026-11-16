using Aviss.Domain.Entidades;
using Aviss.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aviss.Infrastructure.Persistencia.Configuraciones;

// Cómo se guarda cada entidad. Lo que EF puede expresar (columnas, CHECK, índices, claves) va acá;
// lo que no (triggers) va escrito a mano en la migración "Inicial".
// Convenciones: Id GUID asignado por el dominio, Restrict en toda FK (nada se borra en cascada), enums como texto.

internal static class Comun
{
    /// <summary>
    /// "Clave" de un texto: mayúsculas SIN distinguir la Ñ ni las vocales con tilde. SQLite upper() solo entiende ASCII
    /// (y COLLATE NOCASE tampoco), así que la Ñ se pasa a mano. DEBE dar lo mismo que Caravana.CalcularClave de C#
    /// (hay un test que lo comprueba).
    /// </summary>
    public static string ClaveSql(string columna)
        => $"replace(replace(replace(replace(replace(replace(replace(upper({columna}),'ñ','Ñ'),'á','Á'),'é','É'),'í','Í'),'ó','Ó'),'ú','Ú'),'ü','Ü')";

    public static void ConfigurarId<T>(EntityTypeBuilder<T> b, string tabla) where T : Entidad
    {
        b.ToTable(tabla);
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).ValueGeneratedNever(); // el GUID lo genera el dominio, nunca la base
    }
}

internal sealed class EstablecimientoConfig : IEntityTypeConfiguration<Establecimiento>
{
    public void Configure(EntityTypeBuilder<Establecimiento> b)
    {
        Comun.ConfigurarId(b, "Establecimiento");
        b.Property(e => e.Nombre).IsRequired();
        b.HasCheckConstraint("CK_Establecimiento_Nombre", "length(trim(Nombre)) > 0");
    }
}

internal sealed class RodeoConfig : IEntityTypeConfiguration<Rodeo>
{
    public void Configure(EntityTypeBuilder<Rodeo> b)
    {
        Comun.ConfigurarId(b, "Rodeo");
        b.Property(e => e.Nombre).IsRequired();
        b.Property(e => e.ArchivoOrigenNombre).IsRequired();
        b.Property(e => e.ArchivoOrigenHash).IsRequired();
        b.Property(e => e.ArchivoOrigenBlob).IsRequired();
        b.Property(e => e.PerfilExcel).IsRequired();
        b.Property(e => e.Estado).HasConversion<string>().IsRequired();

        // Sin índice único en la huella: el mismo archivo importado dos veces son dos rodeos (RF-2).
        b.HasOne<Establecimiento>().WithMany().HasForeignKey(e => e.EstablecimientoId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => e.EstablecimientoId);

        b.HasCheckConstraint("CK_Rodeo_Hash", "length(ArchivoOrigenHash) = 64");
        b.HasCheckConstraint("CK_Rodeo_Archivo", "length(ArchivoOrigenBlob) > 0");
        b.HasCheckConstraint("CK_Rodeo_Estado", "Estado IN ('Abierto','Exportado')");
    }
}

internal sealed class UsuarioConfig : IEntityTypeConfiguration<Usuario>
{
    public void Configure(EntityTypeBuilder<Usuario> b)
    {
        Comun.ConfigurarId(b, "Usuario");
        b.Property(e => e.Nombre).IsRequired();
        b.HasCheckConstraint("CK_Usuario_Nombre", "length(trim(Nombre)) > 0");
        b.HasCheckConstraint("CK_Usuario_Matricula", "Matricula IS NULL OR length(Matricula) <= 30");
    }
}

internal sealed class EstadoReproductivoConfig : IEntityTypeConfiguration<EstadoReproductivo>
{
    public void Configure(EntityTypeBuilder<EstadoReproductivo> b)
    {
        Comun.ConfigurarId(b, "EstadoReproductivo");
        b.Property(e => e.Codigo).IsRequired();
        b.Property(e => e.Nombre).IsRequired();
        b.Property(e => e.CodigoDairyComp).IsRequired();

        // NombreClave es una propiedad de solo lectura en C#: EF no puede escribirla. Se mapea una propiedad "sombra"
        // (existe solo para EF) a la columna generada NombreClave; la base la calcula sola y EF nunca la escribe.
        b.Property<string>("NombreClaveBd")
            .HasColumnName("NombreClave")
            .HasComputedColumnSql(Comun.ClaveSql("Nombre"), stored: true);

        b.HasIndex(e => e.Codigo).IsUnique();
        b.HasIndex("NombreClaveBd").IsUnique();
        // El código de Dairy Comp solo es único entre los activos: uno dado de baja libera su código.
        b.HasIndex(e => e.CodigoDairyComp).IsUnique().HasFilter("Activo = 1");

        b.HasCheckConstraint("CK_Estado_Codigo", "length(Codigo) > 0 AND Codigo NOT GLOB '*[^A-Z0-9_]*'");
    }
}

internal sealed class AnimalConfig : IEntityTypeConfiguration<Animal>
{
    public void Configure(EntityTypeBuilder<Animal> b)
    {
        Comun.ConfigurarId(b, "Animal");
        b.Property(e => e.NumeroCaravana).IsRequired();
        b.Property(e => e.Origen).HasConversion<string>().IsRequired();

        // Columna generada con la clave de la caravana: así "ñ1" y "Ñ1" chocan en el índice único (RF-4).
        b.Property<string>("NumeroCaravanaClave")
            .HasComputedColumnSql(Comun.ClaveSql("NumeroCaravana"), stored: true);

        b.HasOne<Rodeo>().WithMany().HasForeignKey(e => e.RodeoId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<EstadoReproductivo>().WithMany().HasForeignKey(e => e.EstadoReproductivoId).OnDelete(DeleteBehavior.Restrict);

        // Únicos parciales: un animal anulado libera su caravana y su identificador electrónico.
        // Este índice sirve también para buscar por caravana: las consultas deben filtrar Anulado = 0.
        b.HasIndex(nameof(Animal.RodeoId), "NumeroCaravanaClave").IsUnique().HasFilter("Anulado = 0");
        b.HasIndex(e => new { e.RodeoId, e.IdElectronico }).IsUnique().HasFilter("IdElectronico IS NOT NULL AND Anulado = 0");
        b.HasIndex(e => e.EstadoReproductivoId);

        b.HasCheckConstraint("CK_Animal_Caravana", "length(NumeroCaravana) BETWEEN 1 AND 20 AND NumeroCaravana = trim(NumeroCaravana)");
        b.HasCheckConstraint("CK_Animal_IdElectronico", "IdElectronico IS NULL OR (length(IdElectronico) BETWEEN 1 AND 32 AND instr(IdElectronico, ' ') = 0)");
        b.HasCheckConstraint("CK_Animal_Origen", "Origen IN ('Importado','AltaEnCampo')");
        b.HasCheckConstraint("CK_Animal_Anulado", "Anulado IN (0,1) AND (Anulado = 0 OR Origen = 'AltaEnCampo')");
    }
}

internal sealed class TactoConfig : IEntityTypeConfiguration<Tacto>
{
    public void Configure(EntityTypeBuilder<Tacto> b)
    {
        Comun.ConfigurarId(b, "Tacto");
        b.Property(e => e.Tipo).HasConversion<string>().IsRequired();
        // Nulable a propósito: un tacto de anulación puede devolver a un animal sin estado (RF-13).
        // El CHECK de abajo exige estado nuevo en los tactos Normal.
        b.Property(e => e.EstadoNuevoId);

        b.HasOne<Animal>().WithMany().HasForeignKey(e => e.AnimalId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Usuario>().WithMany().HasForeignKey(e => e.UsuarioId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<EstadoReproductivo>().WithMany().HasForeignKey(e => e.EstadoAnteriorId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<EstadoReproductivo>().WithMany().HasForeignKey(e => e.EstadoNuevoId).OnDelete(DeleteBehavior.Restrict);

        // Clave alternativa (AnimalId, Id) + FK compuesta (AnimalId, TactoAnuladoId): la anulación solo puede apuntar a
        // un tacto del MISMO animal (RF-16). Con TactoAnuladoId nulo, SQLite no evalúa la FK.
        b.HasAlternateKey(e => new { e.AnimalId, e.Id });
        b.HasOne<Tacto>().WithMany().HasForeignKey(e => new { e.AnimalId, e.TactoAnuladoId })
            .HasPrincipalKey(e => new { e.AnimalId, e.Id }).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(e => e.OrdenInsercion).IsUnique();
        // Para encontrar rápido el "último tacto vigente" de un animal.
        b.HasIndex(e => new { e.AnimalId, e.FechaHoraUtc, e.OrdenInsercion });
        // Un tacto se anula una sola vez.
        b.HasIndex(e => e.TactoAnuladoId).IsUnique().HasFilter("TactoAnuladoId IS NOT NULL");
        b.HasIndex(e => e.EstadoAnteriorId);
        b.HasIndex(e => e.EstadoNuevoId);
        b.HasIndex(e => e.UsuarioId);

        b.HasCheckConstraint("CK_Tacto_Dias", "DiasPrenez IS NULL OR DiasPrenez BETWEEN 0 AND 300");
        b.HasCheckConstraint("CK_Tacto_Tipo", "Tipo IN ('Normal','Anulacion')");
        b.HasCheckConstraint("CK_Tacto_Anulacion", "(Tipo = 'Anulacion') = (TactoAnuladoId IS NOT NULL)");
        b.HasCheckConstraint("CK_Tacto_NoAnulaAnulacion", "Tipo = 'Normal' OR Anulado = 0");
        b.HasCheckConstraint("CK_Tacto_EstadoNuevo", "Tipo = 'Anulacion' OR EstadoNuevoId IS NOT NULL");
    }
}

internal sealed class HistorialConfig : IEntityTypeConfiguration<Historial>
{
    public void Configure(EntityTypeBuilder<Historial> b)
    {
        Comun.ConfigurarId(b, "Historial");
        b.Property(e => e.Entidad).IsRequired();
        b.Property(e => e.Campo).IsRequired();
        b.Property(e => e.Accion).HasConversion<string>().IsRequired();

        b.HasOne<Usuario>().WithMany().HasForeignKey(e => e.UsuarioId).OnDelete(DeleteBehavior.Restrict);
        // EntidadId NO tiene FK: apunta a tablas distintas según Entidad.
        b.HasIndex(e => new { e.Entidad, e.EntidadId });

        b.HasCheckConstraint("CK_Historial_Accion", "Accion IN ('Alta','Edicion','Tacto','AnulacionTacto','AnulacionAlta')");
    }
}
