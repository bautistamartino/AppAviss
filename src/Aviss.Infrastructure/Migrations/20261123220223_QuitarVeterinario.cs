using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aviss.Infrastructure.Migrations
{
    /// <summary>
    /// Spec 006: se quita el veterinario (tabla Usuario y todas las columnas UsuarioId, AbiertaPorUsuarioId,
    /// AnuladoPorUsuarioId, ResueltaPorUsuarioId) y el identificador de tablet (DispositivoId). Se conservan todas las
    /// demás columnas y filas, sin tocar ningún valor.
    ///
    /// Todo el Up es SQL a mano (como Mangas y EsperaYRechazo): EF ejecutaría sus reconstrucciones de tabla al final y se
    /// llevaría los triggers. SQLite no puede borrar una columna que está en una clave foránea, un índice, un CHECK o un
    /// trigger, así que cada tabla se reconstruye: CREATE X_nueva, copiar todas las filas, DROP X, RENAME, índices.
    ///
    /// Claves foráneas: NUNCA se apaga foreign_keys (RF-14). Se usa PRAGMA defer_foreign_keys = ON (vale dentro de la
    /// transacción de la migración); al confirmar, SQLite exige que no quede ninguna referencia rota.
    /// Ojo con la jerarquía Manga (padre) y CierreManga / MarcaPasada (hijas): si se borra Manga mientras las hijas viejas
    /// existen, el contador diferido queda en más de cero y el COMMIT falla. Por eso las tres tablas nuevas se crean
    /// primero (las hijas apuntando a Manga_nueva), se borran las hijas viejas y recién después Manga, y se renombran las
    /// tres (el RENAME reescribe las claves foráneas que apuntaban a Manga_nueva).
    ///
    /// Los triggers de las tablas reconstruidas se borran antes (un ALTER TABLE RENAME revisa todo el esquema y falla si un
    /// trigger nombra una tabla que no existe) y se recrean al final sin las comparaciones de usuario y dispositivo.
    /// </summary>
    public partial class QuitarVeterinario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
PRAGMA defer_foreign_keys = ON;");

            migrationBuilder.Sql(@"
DROP TRIGGER TR_Historial_NoModificar;");

            migrationBuilder.Sql(@"
DROP TRIGGER TR_Historial_NoBorrar;");

            migrationBuilder.Sql(@"
DROP TRIGGER TR_Tacto_NoModificar;");

            migrationBuilder.Sql(@"
DROP TRIGGER TR_Tacto_NoBorrar;");

            migrationBuilder.Sql(@"
DROP TRIGGER TR_MarcaPasada_NoModificar;");

            migrationBuilder.Sql(@"
DROP TRIGGER TR_MarcaPasada_NoBorrar;");

            migrationBuilder.Sql(@"
DROP TRIGGER TR_MarcaPasada_MismoRodeo;");

            migrationBuilder.Sql(@"
DROP TRIGGER TR_Manga_NoModificar;");

            migrationBuilder.Sql(@"
DROP TRIGGER TR_Manga_NoBorrar;");

            migrationBuilder.Sql(@"
DROP TRIGGER TR_CierreManga_NoModificar;");

            migrationBuilder.Sql(@"
DROP TRIGGER TR_CierreManga_NoBorrar;");

            migrationBuilder.Sql(@"
DROP TRIGGER TR_Rechazo_NoModificar;");

            migrationBuilder.Sql(@"
DROP TRIGGER TR_Rechazo_NoBorrar;");

            migrationBuilder.Sql(@"
DROP TRIGGER TR_Espera_NoModificar;");

            migrationBuilder.Sql(@"
DROP TRIGGER TR_Espera_NoBorrar;");

            migrationBuilder.Sql(@"
CREATE TABLE ""Manga_nueva"" (
    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_Manga"" PRIMARY KEY,
    ""RodeoId"" TEXT NOT NULL,
    ""Numero"" INTEGER NOT NULL,
    ""CreadoEnUtc"" TEXT NOT NULL,
    CONSTRAINT ""CK_Manga_Numero"" CHECK (Numero >= 1),
    CONSTRAINT ""FK_Manga_Rodeo_RodeoId"" FOREIGN KEY (""RodeoId"") REFERENCES ""Rodeo"" (""Id"") ON DELETE RESTRICT
);");

            migrationBuilder.Sql(@"
INSERT INTO ""Manga_nueva"" (""Id"", ""RodeoId"", ""Numero"", ""CreadoEnUtc"") SELECT ""Id"", ""RodeoId"", ""Numero"", ""CreadoEnUtc"" FROM ""Manga"";");

            migrationBuilder.Sql(@"
CREATE TABLE ""CierreManga_nueva"" (
    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_CierreManga"" PRIMARY KEY,
    ""MangaId"" TEXT NOT NULL,
    ""Tipo"" TEXT NOT NULL,
    ""FechaHoraUtc"" TEXT NOT NULL,
    ""CantidadAviss"" INTEGER NOT NULL,
    ""CantidadConfirmada"" INTEGER NULL,
    ""Diferencia"" INTEGER NULL,
    ""Anulado"" INTEGER NOT NULL,
    ""CreadoEnUtc"" TEXT NOT NULL,
    ""ActualizadoEnUtc"" TEXT NOT NULL,
    CONSTRAINT ""CK_CierreManga_Anulado"" CHECK (Anulado IN (0,1)),
    CONSTRAINT ""CK_CierreManga_CantidadAviss"" CHECK (CantidadAviss >= 0),
    CONSTRAINT ""CK_CierreManga_CantidadConfirmada"" CHECK (CantidadConfirmada IS NULL OR CantidadConfirmada >= 0),
    CONSTRAINT ""CK_CierreManga_Diferencia"" CHECK ((CantidadConfirmada IS NULL AND Diferencia IS NULL) OR (CantidadConfirmada IS NOT NULL AND Diferencia = CantidadConfirmada - CantidadAviss)),
    CONSTRAINT ""CK_CierreManga_Tipo"" CHECK (Tipo IN ('Manual','TerminoElTacto','Automatico')),
    CONSTRAINT ""FK_CierreManga_Manga_MangaId"" FOREIGN KEY (""MangaId"") REFERENCES ""Manga_nueva"" (""Id"") ON DELETE RESTRICT
);");

            migrationBuilder.Sql(@"
INSERT INTO ""CierreManga_nueva"" (""Id"", ""MangaId"", ""Tipo"", ""FechaHoraUtc"", ""CantidadAviss"", ""CantidadConfirmada"", ""Diferencia"", ""Anulado"", ""CreadoEnUtc"", ""ActualizadoEnUtc"")
SELECT ""Id"", ""MangaId"", ""Tipo"", ""FechaHoraUtc"", ""CantidadAviss"", ""CantidadConfirmada"", ""Diferencia"", ""Anulado"", ""CreadoEnUtc"", ""ActualizadoEnUtc"" FROM ""CierreManga"";");

            migrationBuilder.Sql(@"
CREATE TABLE ""MarcaPasada_nueva"" (
    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_MarcaPasada"" PRIMARY KEY,
    ""AnimalId"" TEXT NOT NULL,
    ""MangaId"" TEXT NOT NULL,
    ""FechaHoraUtc"" TEXT NOT NULL,
    ""OrdenInsercion"" INTEGER NOT NULL,
    ""Anulado"" INTEGER NOT NULL,
    ""CreadoEnUtc"" TEXT NOT NULL,
    ""ActualizadoEnUtc"" TEXT NOT NULL,
    CONSTRAINT ""CK_MarcaPasada_Anulado"" CHECK (Anulado IN (0,1)),
    CONSTRAINT ""FK_MarcaPasada_Animal_AnimalId"" FOREIGN KEY (""AnimalId"") REFERENCES ""Animal"" (""Id"") ON DELETE RESTRICT,
    CONSTRAINT ""FK_MarcaPasada_Manga_MangaId"" FOREIGN KEY (""MangaId"") REFERENCES ""Manga_nueva"" (""Id"") ON DELETE RESTRICT
);");

            migrationBuilder.Sql(@"
INSERT INTO ""MarcaPasada_nueva"" (""Id"", ""AnimalId"", ""MangaId"", ""FechaHoraUtc"", ""OrdenInsercion"", ""Anulado"", ""CreadoEnUtc"", ""ActualizadoEnUtc"")
SELECT ""Id"", ""AnimalId"", ""MangaId"", ""FechaHoraUtc"", ""OrdenInsercion"", ""Anulado"", ""CreadoEnUtc"", ""ActualizadoEnUtc"" FROM ""MarcaPasada"";");

            migrationBuilder.Sql(@"
DROP TABLE ""CierreManga"";");

            migrationBuilder.Sql(@"
DROP TABLE ""MarcaPasada"";");

            migrationBuilder.Sql(@"
DROP TABLE ""Manga"";");

            migrationBuilder.Sql(@"
ALTER TABLE ""Manga_nueva"" RENAME TO ""Manga"";");

            migrationBuilder.Sql(@"
ALTER TABLE ""CierreManga_nueva"" RENAME TO ""CierreManga"";");

            migrationBuilder.Sql(@"
ALTER TABLE ""MarcaPasada_nueva"" RENAME TO ""MarcaPasada"";");

            migrationBuilder.Sql(@"
CREATE UNIQUE INDEX ""IX_Manga_RodeoId_Numero"" ON ""Manga"" (""RodeoId"", ""Numero"");");

            migrationBuilder.Sql(@"
CREATE UNIQUE INDEX ""IX_CierreManga_MangaId"" ON ""CierreManga"" (""MangaId"") WHERE Anulado = 0;");

            migrationBuilder.Sql(@"
CREATE INDEX ""IX_MarcaPasada_AnimalId_FechaHoraUtc_OrdenInsercion"" ON ""MarcaPasada"" (""AnimalId"", ""FechaHoraUtc"", ""OrdenInsercion"");");

            migrationBuilder.Sql(@"
CREATE INDEX ""IX_MarcaPasada_FechaHoraUtc"" ON ""MarcaPasada"" (""FechaHoraUtc"");");

            migrationBuilder.Sql(@"
CREATE UNIQUE INDEX ""IX_MarcaPasada_MangaId_AnimalId"" ON ""MarcaPasada"" (""MangaId"", ""AnimalId"") WHERE Anulado = 0;");

            migrationBuilder.Sql(@"
CREATE UNIQUE INDEX ""IX_MarcaPasada_OrdenInsercion"" ON ""MarcaPasada"" (""OrdenInsercion"");");

            migrationBuilder.Sql(@"
CREATE TABLE ""Tacto_nueva"" (
    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_Tacto"" PRIMARY KEY,
    ""AnimalId"" TEXT NOT NULL,
    ""FechaHoraUtc"" TEXT NOT NULL,
    ""OrdenInsercion"" INTEGER NOT NULL,
    ""EstadoAnteriorId"" TEXT NULL,
    ""EstadoNuevoId"" TEXT NULL,
    ""DiasPrenez"" INTEGER NULL,
    ""Observaciones"" TEXT NULL,
    ""Tipo"" TEXT NOT NULL,
    ""TactoAnuladoId"" TEXT NULL,
    ""Anulado"" INTEGER NOT NULL,
    ""CreadoEnUtc"" TEXT NOT NULL,
    ""ActualizadoEnUtc"" TEXT NOT NULL,
    CONSTRAINT ""AK_Tacto_AnimalId_Id"" UNIQUE (""AnimalId"", ""Id""),
    CONSTRAINT ""CK_Tacto_Anulacion"" CHECK ((Tipo = 'Anulacion') = (TactoAnuladoId IS NOT NULL)),
    CONSTRAINT ""CK_Tacto_Dias"" CHECK (DiasPrenez IS NULL OR DiasPrenez BETWEEN 0 AND 300),
    CONSTRAINT ""CK_Tacto_EstadoNuevo"" CHECK (Tipo = 'Anulacion' OR EstadoNuevoId IS NOT NULL),
    CONSTRAINT ""CK_Tacto_NoAnulaAnulacion"" CHECK (Tipo = 'Normal' OR Anulado = 0),
    CONSTRAINT ""CK_Tacto_Tipo"" CHECK (Tipo IN ('Normal','Anulacion')),
    CONSTRAINT ""FK_Tacto_Animal_AnimalId"" FOREIGN KEY (""AnimalId"") REFERENCES ""Animal"" (""Id"") ON DELETE RESTRICT,
    CONSTRAINT ""FK_Tacto_EstadoReproductivo_EstadoAnteriorId"" FOREIGN KEY (""EstadoAnteriorId"") REFERENCES ""EstadoReproductivo"" (""Id"") ON DELETE RESTRICT,
    CONSTRAINT ""FK_Tacto_EstadoReproductivo_EstadoNuevoId"" FOREIGN KEY (""EstadoNuevoId"") REFERENCES ""EstadoReproductivo"" (""Id"") ON DELETE RESTRICT,
    CONSTRAINT ""FK_Tacto_Tacto_AnimalId_TactoAnuladoId"" FOREIGN KEY (""AnimalId"", ""TactoAnuladoId"") REFERENCES ""Tacto_nueva"" (""AnimalId"", ""Id"") ON DELETE RESTRICT
);");

            migrationBuilder.Sql(@"
INSERT INTO ""Tacto_nueva"" (""Id"", ""AnimalId"", ""FechaHoraUtc"", ""OrdenInsercion"", ""EstadoAnteriorId"", ""EstadoNuevoId"", ""DiasPrenez"", ""Observaciones"", ""Tipo"", ""TactoAnuladoId"", ""Anulado"", ""CreadoEnUtc"", ""ActualizadoEnUtc"")
SELECT ""Id"", ""AnimalId"", ""FechaHoraUtc"", ""OrdenInsercion"", ""EstadoAnteriorId"", ""EstadoNuevoId"", ""DiasPrenez"", ""Observaciones"", ""Tipo"", ""TactoAnuladoId"", ""Anulado"", ""CreadoEnUtc"", ""ActualizadoEnUtc"" FROM ""Tacto""
ORDER BY ""OrdenInsercion"";");

            migrationBuilder.Sql(@"
DROP TABLE ""Tacto"";");

            migrationBuilder.Sql(@"
ALTER TABLE ""Tacto_nueva"" RENAME TO ""Tacto"";");

            migrationBuilder.Sql(@"
CREATE INDEX ""IX_Tacto_AnimalId_FechaHoraUtc_OrdenInsercion"" ON ""Tacto"" (""AnimalId"", ""FechaHoraUtc"", ""OrdenInsercion"");");

            migrationBuilder.Sql(@"
CREATE INDEX ""IX_Tacto_AnimalId_TactoAnuladoId"" ON ""Tacto"" (""AnimalId"", ""TactoAnuladoId"");");

            migrationBuilder.Sql(@"
CREATE INDEX ""IX_Tacto_EstadoAnteriorId"" ON ""Tacto"" (""EstadoAnteriorId"");");

            migrationBuilder.Sql(@"
CREATE INDEX ""IX_Tacto_EstadoNuevoId"" ON ""Tacto"" (""EstadoNuevoId"");");

            migrationBuilder.Sql(@"
CREATE INDEX ""IX_Tacto_FechaHoraUtc"" ON ""Tacto"" (""FechaHoraUtc"");");

            migrationBuilder.Sql(@"
CREATE UNIQUE INDEX ""IX_Tacto_OrdenInsercion"" ON ""Tacto"" (""OrdenInsercion"");");

            migrationBuilder.Sql(@"
CREATE UNIQUE INDEX ""IX_Tacto_TactoAnuladoId"" ON ""Tacto"" (""TactoAnuladoId"") WHERE TactoAnuladoId IS NOT NULL;");

            migrationBuilder.Sql(@"
CREATE TABLE ""Rechazo_nueva"" (
    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_Rechazo"" PRIMARY KEY,
    ""AnimalId"" TEXT NOT NULL,
    ""FechaHoraUtc"" TEXT NOT NULL,
    ""OrdenInsercion"" INTEGER NOT NULL,
    ""Observaciones"" TEXT NULL,
    ""Anulado"" INTEGER NOT NULL,
    ""AnuladoEnUtc"" TEXT NULL,
    ""CreadoEnUtc"" TEXT NOT NULL,
    ""ActualizadoEnUtc"" TEXT NOT NULL,
    CONSTRAINT ""CK_Rechazo_Anulacion"" CHECK ((Anulado = 0 AND AnuladoEnUtc IS NULL) OR (Anulado = 1 AND AnuladoEnUtc IS NOT NULL)),
    CONSTRAINT ""CK_Rechazo_Anulado"" CHECK (Anulado IN (0,1)),
    CONSTRAINT ""FK_Rechazo_Animal_AnimalId"" FOREIGN KEY (""AnimalId"") REFERENCES ""Animal"" (""Id"") ON DELETE RESTRICT
);");

            migrationBuilder.Sql(@"
INSERT INTO ""Rechazo_nueva"" (""Id"", ""AnimalId"", ""FechaHoraUtc"", ""OrdenInsercion"", ""Observaciones"", ""Anulado"", ""AnuladoEnUtc"", ""CreadoEnUtc"", ""ActualizadoEnUtc"")
SELECT ""Id"", ""AnimalId"", ""FechaHoraUtc"", ""OrdenInsercion"", ""Observaciones"", ""Anulado"", ""AnuladoEnUtc"", ""CreadoEnUtc"", ""ActualizadoEnUtc"" FROM ""Rechazo"";");

            migrationBuilder.Sql(@"
DROP TABLE ""Rechazo"";");

            migrationBuilder.Sql(@"
ALTER TABLE ""Rechazo_nueva"" RENAME TO ""Rechazo"";");

            migrationBuilder.Sql(@"
CREATE INDEX ""IX_Rechazo_AnimalId_FechaHoraUtc_OrdenInsercion"" ON ""Rechazo"" (""AnimalId"", ""FechaHoraUtc"", ""OrdenInsercion"");");

            migrationBuilder.Sql(@"
CREATE UNIQUE INDEX ""IX_Rechazo_OrdenInsercion"" ON ""Rechazo"" (""OrdenInsercion"");");

            migrationBuilder.Sql(@"
CREATE TABLE ""Espera_nueva"" (
    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_Espera"" PRIMARY KEY,
    ""AnimalId"" TEXT NOT NULL,
    ""FechaHoraUtc"" TEXT NOT NULL,
    ""Estado"" TEXT NOT NULL,
    ""ResueltaEnUtc"" TEXT NULL,
    ""RegistroResolucionId"" TEXT NULL,
    ""CreadoEnUtc"" TEXT NOT NULL,
    ""ActualizadoEnUtc"" TEXT NOT NULL,
    CONSTRAINT ""CK_Espera_Estado"" CHECK (Estado IN ('Pendiente','Resuelta','Salteada')),
    CONSTRAINT ""CK_Espera_Resolucion"" CHECK ((Estado = 'Pendiente' AND ResueltaEnUtc IS NULL AND RegistroResolucionId IS NULL) OR (Estado = 'Resuelta' AND ResueltaEnUtc IS NOT NULL AND RegistroResolucionId IS NOT NULL) OR (Estado = 'Salteada' AND ResueltaEnUtc IS NOT NULL AND RegistroResolucionId IS NULL)),
    CONSTRAINT ""FK_Espera_Animal_AnimalId"" FOREIGN KEY (""AnimalId"") REFERENCES ""Animal"" (""Id"") ON DELETE RESTRICT
);");

            migrationBuilder.Sql(@"
INSERT INTO ""Espera_nueva"" (""Id"", ""AnimalId"", ""FechaHoraUtc"", ""Estado"", ""ResueltaEnUtc"", ""RegistroResolucionId"", ""CreadoEnUtc"", ""ActualizadoEnUtc"")
SELECT ""Id"", ""AnimalId"", ""FechaHoraUtc"", ""Estado"", ""ResueltaEnUtc"", ""RegistroResolucionId"", ""CreadoEnUtc"", ""ActualizadoEnUtc"" FROM ""Espera"";");

            migrationBuilder.Sql(@"
DROP TABLE ""Espera"";");

            migrationBuilder.Sql(@"
ALTER TABLE ""Espera_nueva"" RENAME TO ""Espera"";");

            migrationBuilder.Sql(@"
CREATE UNIQUE INDEX ""IX_Espera_AnimalId"" ON ""Espera"" (""AnimalId"") WHERE Estado = 'Pendiente';");

            migrationBuilder.Sql(@"
CREATE TABLE ""Historial_nueva"" (
    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_Historial"" PRIMARY KEY,
    ""Accion"" TEXT NOT NULL,
    ""Campo"" TEXT NOT NULL,
    ""CreadoEnUtc"" TEXT NOT NULL,
    ""Entidad"" TEXT NOT NULL,
    ""EntidadId"" TEXT NOT NULL,
    ""FechaHoraUtc"" TEXT NOT NULL,
    ""ValorAnterior"" TEXT NULL,
    ""ValorNuevo"" TEXT NULL,
    CONSTRAINT ""CK_Historial_Accion"" CHECK (Accion IN ('Alta','Edicion','Tacto','AnulacionTacto','AnulacionAlta','MarcaPasada','AnulacionMarca','MangaAbierta','MangaCerrada','AnulacionCierre','EsperaCreada','EsperaResuelta','EsperaSalteada','RechazoCreado','RechazoAnulado'))
);");

            migrationBuilder.Sql(@"
INSERT INTO ""Historial_nueva"" (""Id"", ""Accion"", ""Campo"", ""CreadoEnUtc"", ""Entidad"", ""EntidadId"", ""FechaHoraUtc"", ""ValorAnterior"", ""ValorNuevo"")
SELECT ""Id"", ""Accion"", ""Campo"", ""CreadoEnUtc"", ""Entidad"", ""EntidadId"", ""FechaHoraUtc"", ""ValorAnterior"", ""ValorNuevo"" FROM ""Historial"";");

            migrationBuilder.Sql(@"
DROP TABLE ""Historial"";");

            migrationBuilder.Sql(@"
ALTER TABLE ""Historial_nueva"" RENAME TO ""Historial"";");

            migrationBuilder.Sql(@"
CREATE INDEX ""IX_Historial_Entidad_EntidadId"" ON ""Historial"" (""Entidad"", ""EntidadId"");");

            migrationBuilder.Sql(@"
DROP TABLE ""Usuario"";");

            migrationBuilder.Sql(@"
CREATE TRIGGER TR_Historial_NoBorrar BEFORE DELETE ON Historial
BEGIN
    SELECT RAISE(ABORT, 'El historial no se puede borrar.');
END;");

            migrationBuilder.Sql(@"
CREATE TRIGGER TR_Historial_NoModificar BEFORE UPDATE ON Historial
BEGIN
    SELECT RAISE(ABORT, 'El historial no se puede modificar.');
END;");

            migrationBuilder.Sql(@"
CREATE TRIGGER TR_Tacto_NoBorrar BEFORE DELETE ON Tacto
BEGIN
    SELECT RAISE(ABORT, 'Un tacto no se puede borrar.');
END;");

            migrationBuilder.Sql(@"
CREATE TRIGGER TR_Tacto_NoModificar BEFORE UPDATE ON Tacto
WHEN NOT (
        OLD.Anulado = 0 AND NEW.Anulado = 1
    AND OLD.Id IS NEW.Id
    AND OLD.CreadoEnUtc IS NEW.CreadoEnUtc
    AND OLD.AnimalId IS NEW.AnimalId
    AND OLD.FechaHoraUtc IS NEW.FechaHoraUtc
    AND OLD.OrdenInsercion IS NEW.OrdenInsercion
    AND OLD.EstadoAnteriorId IS NEW.EstadoAnteriorId
    AND OLD.EstadoNuevoId IS NEW.EstadoNuevoId
    AND OLD.DiasPrenez IS NEW.DiasPrenez
    AND OLD.Observaciones IS NEW.Observaciones
    AND OLD.Tipo IS NEW.Tipo
    AND OLD.TactoAnuladoId IS NEW.TactoAnuladoId)
BEGIN
    SELECT RAISE(ABORT, 'Un tacto guardado no se puede modificar; solo se puede anular.');
END;");

            migrationBuilder.Sql(@"
CREATE TRIGGER TR_MarcaPasada_MismoRodeo BEFORE INSERT ON MarcaPasada
WHEN (SELECT RodeoId FROM Animal WHERE Id = NEW.AnimalId) IS NOT (SELECT RodeoId FROM Manga WHERE Id = NEW.MangaId)
BEGIN
    SELECT RAISE(ABORT, 'La vaca y la manga tienen que ser del mismo rodeo.');
END;");

            migrationBuilder.Sql(@"
CREATE TRIGGER TR_MarcaPasada_NoBorrar BEFORE DELETE ON MarcaPasada
BEGIN
    SELECT RAISE(ABORT, 'Una marca de pasada no se puede borrar.');
END;");

            migrationBuilder.Sql(@"
CREATE TRIGGER TR_MarcaPasada_NoModificar BEFORE UPDATE ON MarcaPasada
WHEN NOT (
        OLD.Anulado = 0 AND NEW.Anulado = 1
    AND OLD.Id IS NEW.Id
    AND OLD.CreadoEnUtc IS NEW.CreadoEnUtc
    AND OLD.AnimalId IS NEW.AnimalId
    AND OLD.MangaId IS NEW.MangaId
    AND OLD.FechaHoraUtc IS NEW.FechaHoraUtc
    AND OLD.OrdenInsercion IS NEW.OrdenInsercion)
BEGIN
    SELECT RAISE(ABORT, 'Una marca de pasada guardada no se puede modificar; solo se puede anular.');
END;");

            migrationBuilder.Sql(@"
CREATE TRIGGER TR_Manga_NoBorrar BEFORE DELETE ON Manga
BEGIN
    SELECT RAISE(ABORT, 'Una manga no se puede borrar.');
END;");

            migrationBuilder.Sql(@"
CREATE TRIGGER TR_Manga_NoModificar BEFORE UPDATE ON Manga
BEGIN
    SELECT RAISE(ABORT, 'Una manga guardada no se puede modificar.');
END;");

            migrationBuilder.Sql(@"
CREATE TRIGGER TR_CierreManga_NoBorrar BEFORE DELETE ON CierreManga
BEGIN
    SELECT RAISE(ABORT, 'Un cierre de manga no se puede borrar.');
END;");

            migrationBuilder.Sql(@"
CREATE TRIGGER TR_CierreManga_NoModificar BEFORE UPDATE ON CierreManga
WHEN NOT (
        OLD.Anulado = 0 AND NEW.Anulado = 1
    AND OLD.Id IS NEW.Id
    AND OLD.CreadoEnUtc IS NEW.CreadoEnUtc
    AND OLD.MangaId IS NEW.MangaId
    AND OLD.Tipo IS NEW.Tipo
    AND OLD.FechaHoraUtc IS NEW.FechaHoraUtc
    AND OLD.CantidadAviss IS NEW.CantidadAviss
    AND OLD.CantidadConfirmada IS NEW.CantidadConfirmada
    AND OLD.Diferencia IS NEW.Diferencia)
BEGIN
    SELECT RAISE(ABORT, 'Un cierre de manga guardado no se puede modificar; solo se puede anular.');
END;");

            migrationBuilder.Sql(@"
CREATE TRIGGER TR_Rechazo_NoBorrar BEFORE DELETE ON Rechazo
BEGIN
    SELECT RAISE(ABORT, 'Un rechazo no se puede borrar.');
END;");

            migrationBuilder.Sql(@"
CREATE TRIGGER TR_Rechazo_NoModificar BEFORE UPDATE ON Rechazo
WHEN NOT (
        OLD.Anulado = 0 AND NEW.Anulado = 1
    AND OLD.Id IS NEW.Id
    AND OLD.CreadoEnUtc IS NEW.CreadoEnUtc
    AND OLD.AnimalId IS NEW.AnimalId
    AND OLD.FechaHoraUtc IS NEW.FechaHoraUtc
    AND OLD.OrdenInsercion IS NEW.OrdenInsercion
    AND OLD.Observaciones IS NEW.Observaciones)
BEGIN
    SELECT RAISE(ABORT, 'Un rechazo guardado no se puede modificar; solo se puede anular.');
END;");

            migrationBuilder.Sql(@"
CREATE TRIGGER TR_Espera_NoBorrar BEFORE DELETE ON Espera
BEGIN
    SELECT RAISE(ABORT, 'Una espera no se puede borrar.');
END;");

            migrationBuilder.Sql(@"
CREATE TRIGGER TR_Espera_NoModificar BEFORE UPDATE ON Espera
WHEN NOT (
        OLD.Estado = 'Pendiente' AND NEW.Estado IN ('Resuelta','Salteada')
    AND OLD.Id IS NEW.Id
    AND OLD.CreadoEnUtc IS NEW.CreadoEnUtc
    AND OLD.AnimalId IS NEW.AnimalId
    AND OLD.FechaHoraUtc IS NEW.FechaHoraUtc)
BEGIN
    SELECT RAISE(ABORT, 'Una espera guardada no se puede modificar; solo se puede resolver o saltear, una vez.');
END;");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Esta migración descarta datos (quién y qué tablet) y recrear Usuario con columnas NOT NULL sería inventar datos.
            // Nunca se llama a Down desde la app: el camino de vuelta es la copia previa que hace el arranque (spec 001, RF-19).
            throw new NotSupportedException(
                "Esta migración descarta datos y no se puede deshacer; se vuelve atrás con la copia previa.");
        }
    }
}
