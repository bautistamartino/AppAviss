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

        // Spec 005 (migración "EventosDelCatalogo", escrita a mano): tipo, principal, orden e identidad estándar.
        // Todas con valor por defecto en la base, así las filas viejas quedan válidas.
        b.Property(e => e.Tipo).HasConversion<string>().IsRequired().HasDefaultValue(TipoEvento.Estado);
        b.Property(e => e.EsPrincipal).HasDefaultValue(false);
        b.Property(e => e.Orden).HasDefaultValue(0);
        b.Property(e => e.ClaveEstandar);
        // Un evento estándar no se puede duplicar: la clave es única entre los que la tienen (los creados a mano no).
        b.HasIndex(e => e.ClaveEstandar).IsUnique().HasFilter("ClaveEstandar IS NOT NULL");

        b.HasCheckConstraint("CK_Estado_Tipo", "Tipo IN ('Estado','SoloEvento')");
        b.HasCheckConstraint("CK_Estado_EsPrincipal", "EsPrincipal IN (0,1)");
        b.HasCheckConstraint("CK_Estado_Orden", "\"Orden\" >= 0");
        b.HasCheckConstraint("CK_Estado_ClaveEstandar",
            "ClaveEstandar IS NULL OR (length(ClaveEstandar) > 0 AND ClaveEstandar NOT GLOB '*[^A-Z0-9_]*')");
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
        // Para el avance del día y los últimos movimientos (tactos dentro de un rango de fechas, spec 003): sin este índice
        // SQLite recorre todos los tactos y con 100.000 registros el avance pasaba de los 200 ms de RF-37.
        b.HasIndex(e => e.FechaHoraUtc);
        // Un tacto se anula una sola vez.
        b.HasIndex(e => e.TactoAnuladoId).IsUnique().HasFilter("TactoAnuladoId IS NOT NULL");
        b.HasIndex(e => e.EstadoAnteriorId);
        b.HasIndex(e => e.EstadoNuevoId);

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

        // EntidadId NO tiene FK: apunta a tablas distintas según Entidad.
        b.HasIndex(e => new { e.Entidad, e.EntidadId });

        b.HasCheckConstraint("CK_Historial_Accion", "Accion IN ('Alta','Edicion','Tacto','AnulacionTacto','AnulacionAlta','MarcaPasada','AnulacionMarca','MangaAbierta','MangaCerrada','AnulacionCierre','EsperaCreada','EsperaResuelta','EsperaSalteada','RechazoCreado','RechazoAnulado')");
    }
}

// ---------------------------------------------------------------------------------------------------------------
// Spec 003: mangas y marcas de pasada. Los triggers y el CHECK del historial reescrito van a mano en las migraciones (Mangas, EsperaYRechazo, QuitarVeterinario).
// ---------------------------------------------------------------------------------------------------------------

internal sealed class MangaConfig : IEntityTypeConfiguration<Manga>
{
    public void Configure(EntityTypeBuilder<Manga> b)
    {
        Comun.ConfigurarId(b, "Manga");

        b.HasOne<Rodeo>().WithMany().HasForeignKey(e => e.RodeoId).OnDelete(DeleteBehavior.Restrict);

        // El número de manga es único por rodeo: dos tablets no pueden crear la "Manga 3" del mismo rodeo.
        b.HasIndex(e => new { e.RodeoId, e.Numero }).IsUnique();

        b.HasCheckConstraint("CK_Manga_Numero", "Numero >= 1");
    }
}

internal sealed class CierreMangaConfig : IEntityTypeConfiguration<CierreManga>
{
    public void Configure(EntityTypeBuilder<CierreManga> b)
    {
        Comun.ConfigurarId(b, "CierreManga");
        b.Property(e => e.Tipo).HasConversion<string>().IsRequired();

        b.HasOne<Manga>().WithMany().HasForeignKey(e => e.MangaId).OnDelete(DeleteBehavior.Restrict);

        // Una manga no puede tener dos cierres vigentes a la vez (uno anulado y otro nuevo, sí).
        b.HasIndex(e => e.MangaId).IsUnique().HasFilter("Anulado = 0");

        b.HasCheckConstraint("CK_CierreManga_Tipo", "Tipo IN ('Manual','TerminoElTacto','Automatico')");
        b.HasCheckConstraint("CK_CierreManga_CantidadAviss", "CantidadAviss >= 0");
        b.HasCheckConstraint("CK_CierreManga_CantidadConfirmada", "CantidadConfirmada IS NULL OR CantidadConfirmada >= 0");
        // La diferencia siempre es confirmada - Aviss; sin cantidad confirmada no hay diferencia.
        b.HasCheckConstraint("CK_CierreManga_Diferencia",
            "(CantidadConfirmada IS NULL AND Diferencia IS NULL) OR (CantidadConfirmada IS NOT NULL AND Diferencia = CantidadConfirmada - CantidadAviss)");
        b.HasCheckConstraint("CK_CierreManga_Anulado", "Anulado IN (0,1)");
    }
}

internal sealed class MarcaPasadaConfig : IEntityTypeConfiguration<MarcaPasada>
{
    public void Configure(EntityTypeBuilder<MarcaPasada> b)
    {
        Comun.ConfigurarId(b, "MarcaPasada");

        b.HasOne<Animal>().WithMany().HasForeignKey(e => e.AnimalId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Manga>().WithMany().HasForeignKey(e => e.MangaId).OnDelete(DeleteBehavior.Restrict);

        // El orden de guardado es único también acá (entre tablas lo garantiza el cálculo dentro de la transacción).
        b.HasIndex(e => e.OrdenInsercion).IsUnique();
        // Una vaca tiene una sola marca vigente por manga: la base garantiza que el contador cuente vacas distintas (RF-5).
        b.HasIndex(e => new { e.MangaId, e.AnimalId }).IsUnique().HasFilter("Anulado = 0");
        // Para encontrar rápido el último registro vigente de una vaca.
        b.HasIndex(e => new { e.AnimalId, e.FechaHoraUtc, e.OrdenInsercion });
        // Para el avance del día (marcas dentro de un rango de fechas).
        b.HasIndex(e => e.FechaHoraUtc);

        b.HasCheckConstraint("CK_MarcaPasada_Anulado", "Anulado IN (0,1)");
    }
}

// ---------------------------------------------------------------------------------------------------------------
// Spec 004: espera y rechazo. Los triggers y el CHECK del historial reescrito van a mano en la migración "EsperaYRechazo".
// ---------------------------------------------------------------------------------------------------------------

internal sealed class EsperaConfig : IEntityTypeConfiguration<Espera>
{
    public void Configure(EntityTypeBuilder<Espera> b)
    {
        Comun.ConfigurarId(b, "Espera");
        b.Property(e => e.Estado).HasConversion<string>().IsRequired();

        b.HasOne<Animal>().WithMany().HasForeignKey(e => e.AnimalId).OnDelete(DeleteBehavior.Restrict);
        // RegistroResolucionId NO tiene FK: apunta a Tacto o a Rechazo según cómo se resolvió.

        // Una vaca tiene una sola espera pendiente (la vaca es de un solo rodeo, así que es una por rodeo); resueltas o
        // salteadas puede haber muchas.
        b.HasIndex(e => e.AnimalId).IsUnique().HasFilter("Estado = 'Pendiente'");

        b.HasCheckConstraint("CK_Espera_Estado", "Estado IN ('Pendiente','Resuelta','Salteada')");
        // Coherencia de los datos de resolución con el estado.
        b.HasCheckConstraint("CK_Espera_Resolucion",
            "(Estado = 'Pendiente' AND ResueltaEnUtc IS NULL AND RegistroResolucionId IS NULL)"
            + " OR (Estado = 'Resuelta' AND ResueltaEnUtc IS NOT NULL AND RegistroResolucionId IS NOT NULL)"
            + " OR (Estado = 'Salteada' AND ResueltaEnUtc IS NOT NULL AND RegistroResolucionId IS NULL)");
    }
}

internal sealed class RechazoConfig : IEntityTypeConfiguration<Rechazo>
{
    public void Configure(EntityTypeBuilder<Rechazo> b)
    {
        Comun.ConfigurarId(b, "Rechazo");

        b.HasOne<Animal>().WithMany().HasForeignKey(e => e.AnimalId).OnDelete(DeleteBehavior.Restrict);
        // Spec 005: el evento del catálogo (solo evento) que guarda este registro; nulo = el Rechazo fijo.
        // Los triggers que exigen Tipo = 'SoloEvento' y la inmutabilidad de EventoId van a mano en "EventosDelCatalogo".
        b.HasOne<EstadoReproductivo>().WithMany().HasForeignKey(e => e.EventoId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => e.EventoId);

        // Orden de guardado único (compartido con tactos y marcas; entre tablas lo garantiza el cálculo en la transacción).
        b.HasIndex(e => e.OrdenInsercion).IsUnique();
        // Para encontrar rápido el último registro vigente de una vaca.
        b.HasIndex(e => new { e.AnimalId, e.FechaHoraUtc, e.OrdenInsercion });

        b.HasCheckConstraint("CK_Rechazo_Anulado", "Anulado IN (0,1)");
        // Anulado implica tener fecha de anulación; sin anular, no la hay.
        b.HasCheckConstraint("CK_Rechazo_Anulacion",
            "(Anulado = 0 AND AnuladoEnUtc IS NULL)"
            + " OR (Anulado = 1 AND AnuladoEnUtc IS NOT NULL)");
    }
}
