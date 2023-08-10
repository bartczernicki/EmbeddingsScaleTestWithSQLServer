/*
SQL script to build the suporting tables, stored procedures for Wikipedia Vectors Index
*/
drop table if exists dbo.WikipediaPassages;
CREATE TABLE dbo.WikipediaPassages(
	WikiId int NOT NULL,
	Title varchar (200) NOT NULL
 CONSTRAINT pkWikipediaPassages PRIMARY KEY CLUSTERED 
(
	WikiId ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
)
GO

drop table if exists dbo.WikipediaPassagesParagraphs;
CREATE TABLE dbo.WikipediaPassagesParagraphs(
	WikiId int NOT NULL,
	ParagraphId int NOT NULL,
	Paragraph varchar (3000) NOT NULL
 CONSTRAINT pkWikipediaPassagesParagraphs PRIMARY KEY CLUSTERED 
(
	WikiId ASC, ParagraphId ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
)