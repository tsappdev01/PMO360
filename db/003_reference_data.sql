/*  PMO360 — 003_reference_data.sql
    The reference lists of section 4.3: entities, departments, phases and consultants.

    Reference data is data. It is maintained here, by script, so the business can add a
    department or a vendor without a rebuild and a redeploy. Inserts are guarded, so re-running
    adds what is missing and leaves what is there — including anything the PMO has since added.

    Entities are the ones named in section 1 of the BRD. Departments, phases and consultants are
    a starting set: confirm them with the PMO before go-live and extend this script.
*/

SET NOCOUNT ON;
GO

/* Section 1: Dubai Investments PJSC and its reporting entities. */
INSERT INTO pmo.ReportingEntity (Code, Name, SortOrder)
SELECT v.Code, v.Name, v.SortOrder
FROM (VALUES
    ('DI',    N'Dubai Investments PJSC',     10),
    ('DIP',   N'Dubai Investments Park',     20),
    ('DIRE',  N'Dubai Investment Real Estate', 30),
    ('PI',    N'Properties Investment',      40),
    ('ATI',   N'Al Taif Investment',         50)
) AS v (Code, Name, SortOrder)
WHERE NOT EXISTS (SELECT 1 FROM pmo.ReportingEntity e WHERE e.Code = v.Code);
GO

INSERT INTO pmo.Department (Name, SortOrder)
SELECT v.Name, v.SortOrder
FROM (VALUES
    (N'Information Technology', 10),
    (N'Finance',                20),
    (N'Human Resources',        30),
    (N'Procurement',            40),
    (N'Operations',             50),
    (N'Engineering',            60),
    (N'Facilities Management',  70),
    (N'Legal and Compliance',   80),
    (N'Marketing',              90),
    (N'Corporate Affairs',     100)
) AS v (Name, SortOrder)
WHERE NOT EXISTS (SELECT 1 FROM pmo.Department d WHERE d.Name = v.Name);
GO

/* Delivery phases, in the order a project moves through them. */
INSERT INTO pmo.Phase (Name, SortOrder)
SELECT v.Name, v.SortOrder
FROM (VALUES
    (N'Initiation',                  10),
    (N'Requirements',                20),
    (N'Design',                      30),
    (N'Build',                       40),
    (N'Development',                 45),
    (N'System Integration Testing',  50),
    (N'User Acceptance Testing',     60),
    (N'Deployment',                  70),
    (N'Hypercare',                   80),
    (N'Closure',                     90)
) AS v (Name, SortOrder)
WHERE NOT EXISTS (SELECT 1 FROM pmo.Phase p WHERE p.Name = v.Name);
GO

/* FR-04: consultant or vendor from a controlled list. Replace with the real vendor list. */
INSERT INTO pmo.Consultant (Name)
SELECT v.Name
FROM (VALUES (N'Internal IT'), (N'To be appointed')) AS v (Name)
WHERE NOT EXISTS (SELECT 1 FROM pmo.Consultant c WHERE c.Name = v.Name);
GO
