/*  PMO360 — 017_attachment_content.sql
    Document storage inside the database, for when Storage:Provider is Database.

    FR-26 says documents attach to the project record and to an individual update; it does not
    say where the bytes live. Azure Blob Storage is the better home in Azure - cheaper, and it
    keeps large files out of the data file and out of every backup. On an on-premises server with
    no storage account, and in a UAT environment where one database is simpler to move and to
    restore than a database plus a container, this is the alternative.

    pmo.Attachment is the catalogue either way: it carries who attached what, and to which
    project. Only the bytes move. BlobName holds the storage key in both cases - a blob path when
    the provider is Blob, the ContentKey below when it is Database - so nothing else in the
    application has to know which is in use.

    Re-runnable: guarded, and CREATE OR ALTER for the procedures.
*/

IF OBJECT_ID('pmo.AttachmentContent', 'U') IS NULL
CREATE TABLE pmo.AttachmentContent
(
    /* Server-generated, and the value written to pmo.Attachment.BlobName. Never the user's
       file name: an uploaded name must not be able to steer where anything is stored. */
    ContentKey  varchar(400)   NOT NULL CONSTRAINT PK_AttachmentContent PRIMARY KEY,
    Content     varbinary(max) NOT NULL,
    ContentType nvarchar(150)  NOT NULL,
    FileName    nvarchar(260)  NOT NULL,
    SizeBytes   bigint         NOT NULL,
    StoredOn    datetimeoffset(0) NOT NULL
);
GO

/*  Keeps the bytes out of the row, and so out of every query that does not ask for them.
    Without this, a SELECT * over the catalogue drags documents across the wire.
    Guarded because a table that already holds data cannot have this applied - it is set at
    creation - so an existing deployment keeps in-row storage and still works. */
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE object_id = OBJECT_ID('pmo.AttachmentContent')
               AND large_value_types_out_of_row = 1)
   AND NOT EXISTS (SELECT 1 FROM pmo.AttachmentContent)
BEGIN
    EXEC sp_tableoption 'pmo.AttachmentContent', 'large value types out of row', 1;
END;
GO

CREATE OR ALTER PROCEDURE pmo.usp_AttachmentContent_Save
    @ContentKey  varchar(400),
    @Content     varbinary(max),
    @ContentType nvarchar(150),
    @FileName    nvarchar(260),
    @SizeBytes   bigint
AS
BEGIN
    SET NOCOUNT ON;

    /* The key is generated per upload, so a clash means a retry of the same upload rather than
       a different document. Replacing is then the right answer, not an error. */
    IF EXISTS (SELECT 1 FROM pmo.AttachmentContent WHERE ContentKey = @ContentKey)
    BEGIN
        UPDATE pmo.AttachmentContent
        SET Content = @Content,
            ContentType = @ContentType,
            FileName = @FileName,
            SizeBytes = @SizeBytes,
            StoredOn = SYSDATETIMEOFFSET()
        WHERE ContentKey = @ContentKey;
    END
    ELSE
    BEGIN
        INSERT INTO pmo.AttachmentContent (ContentKey, Content, ContentType, FileName, SizeBytes, StoredOn)
        VALUES (@ContentKey, @Content, @ContentType, @FileName, @SizeBytes, SYSDATETIMEOFFSET());
    END;
END;
GO

CREATE OR ALTER PROCEDURE pmo.usp_AttachmentContent_Get
    @ContentKey varchar(400)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Content, ContentType, FileName, SizeBytes
    FROM pmo.AttachmentContent
    WHERE ContentKey = @ContentKey;
END;
GO

/*  What the database is holding in documents. Worth watching: BR-07 keeps everything, so this
    only grows, and a data file that doubles because of meeting papers is a surprise nobody
    wants at a month end. */
CREATE OR ALTER PROCEDURE pmo.usp_AttachmentContent_GetUsage
AS
BEGIN
    SET NOCOUNT ON;

    SELECT COUNT(*) AS DocumentCount,
           ISNULL(SUM(SizeBytes), 0) AS TotalBytes,
           ISNULL(MAX(SizeBytes), 0) AS LargestBytes
    FROM pmo.AttachmentContent;
END;
GO
