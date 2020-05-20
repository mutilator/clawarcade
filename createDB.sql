BEGIN TRANSACTION;
DROP TABLE IF EXISTS "user_prefs";
CREATE TABLE IF NOT EXISTS "user_prefs" (
	"username"	TEXT,
	"scene"	TEXT,
	"lights_on"	INTEGER,
	"last_plush_rename"	INTEGER,
	"first_seen"	INTEGER,
	"knows_multiple"	INTEGER,
	"custom_win_clip"	TEXT,
	"strobe_settings"	TEXT,
	"localization"	TEXT DEFAULT 'en-US',
	"blacklightmode"	INTEGER DEFAULT 0,
	"greenscreen"	TEXT,
	"wiretheme"	TEXT,
	"teamid"	INTEGER,
	"eventteamid"	INTEGER,
	PRIMARY KEY("username"),
	FOREIGN KEY("teamid") REFERENCES "teams"("id"),
	FOREIGN KEY("eventteamid") REFERENCES "teams"("id")
);
DROP TABLE IF EXISTS "teams";
CREATE TABLE IF NOT EXISTS "teams" (
	"id"	INTEGER PRIMARY KEY AUTOINCREMENT,
	"name"	TEXT,
	"guid"	TEXT
);
DROP TABLE IF EXISTS "movement";
CREATE TABLE IF NOT EXISTS "movement" (
	"datetime"	int,
	"name"	VARCHAR(40),
	"direction"	VARCHAR(40),
	"guid"	VARCHAR(40),
	"teamid"	INTEGER
);
DROP TABLE IF EXISTS "wins";
CREATE TABLE IF NOT EXISTS "wins" (
	"datetime"	int,
	"name"	VARCHAR(40),
	"prize"	VARCHAR(40),
	"guid"	VARCHAR(40),
	"PlushID"	INTEGER,
	"teamid"	INTEGER
);
DROP TABLE IF EXISTS "sessions";
CREATE TABLE IF NOT EXISTS "sessions" (
	"datetime"	int,
	"guid"	VARCHAR(40),
	"eventid"	INTEGER,
	"eventname"	TEXT
);
DROP TABLE IF EXISTS "plushie";
CREATE TABLE IF NOT EXISTS "plushie" (
	"ID"	INTEGER,
	"Name"	TEXT,
	"ChangedBy"	TEXT,
	"ChangeDate"	INTEGER,
	"WinStream"	TEXT,
	"BountyStream"	TEXT,
	"BonusBux"	INTEGER,
	"Active"	INTEGER NOT NULL DEFAULT 1,
	PRIMARY KEY("ID")
);
DROP TABLE IF EXISTS "stream_bux_costs";
CREATE TABLE IF NOT EXISTS "stream_bux_costs" (
	"reason"	TEXT,
	"amount"	INTEGER
);
DROP TABLE IF EXISTS "stream_bux";
CREATE TABLE IF NOT EXISTS "stream_bux" (
	"date"	INTEGER,
	"name"	TEXT,
	"reason"	TEXT,
	"amount"	INTEGER
);
DROP TABLE IF EXISTS "plushie_codes";
CREATE TABLE IF NOT EXISTS "plushie_codes" (
	"ID"	INTEGER,
	"EPC"	TEXT,
	"plushID"	INTEGER,
	PRIMARY KEY("ID")
);
DROP INDEX IF EXISTS "nameidx";
CREATE INDEX IF NOT EXISTS "nameidx" ON "movement" (
	"name"
);
DROP INDEX IF EXISTS "winnameidx";
CREATE INDEX IF NOT EXISTS "winnameidx" ON "wins" (
	"name"
);
DROP INDEX IF EXISTS "winplushididx";
CREATE INDEX IF NOT EXISTS "winplushididx" ON "wins" (
	"PlushID"
);
DROP INDEX IF EXISTS "buxnameidx";
CREATE INDEX IF NOT EXISTS "buxnameidx" ON "stream_bux" (
	"name"
);
COMMIT;
