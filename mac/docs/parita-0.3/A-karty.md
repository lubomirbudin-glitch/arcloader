# Proud A — karty a bezpečnost dat (Mac) — stav k 2026-10-08

Větev `worktree-agent-aa4ee1c63ae7477e2` (rychle převinuta na `main` 48b483f — výchozí stav worktree byl o 33 commitů
starší, bez Mac testů). Práce přerušena termínem koordinátora: hotové jsou jen **základy** níže, funkce 1–6 a 10 jako
celek **nejsou** hotové. Žádný řádek ROZDILY zatím nelze přepnout na ✅.

## Hotovo (commit `3cba3a1`, testy zelené)

- **Registr hashů ASC MHL** (`Hash.swift`): `xxh64`, `md5`, `sha1` (CommonCrypto), `xxh3`, `xxh128` (xxHash 0.8.3 v
  CXXHash, kanonický big-endian hex), `c4` (SHA-512 + base58); `HashKind`/`HashKinds` (`xxh64` | `both`, R16 — MD5 jen
  navíc), `FileHashes`, `hashFile(…, kinds:)` = xxh64 + MD5 v jednom čtení. Testy: vektory shodné s Windows
  `HashAlgoTests`, blok po bloku = najednou, MD5 + xxh64 jedním čtením.
- **ASC MHL zápis** (`MHL.swift`): generace N (`NNNN_<karta>_<UTC>Z.mhl`, vytvořena bez přepisu `IO.writeFileNew`),
  komentář doplnění česky jako Windows, ignore vzory z generace 1, prvek `<md5>` se stejným `action`/`hashdate`,
  řetěz přepsaný atomicky se všemi dřívějšími záznamy + nový; odmítne generaci, která nenavazuje na řetěz.
- **ASC MHL čtení** (`MHLReader.swift`, port `MhlReader`): všechny generace, řetěz s kontrolou C4, chybějící/změněná
  generace, soubor mimo řetěz, chybějící řetěz (čtení podle čísla), `previousPath`, cesty s `\`, nejnovější záznam
  vyhrává, `lastGood`, české texty problémů doslova jako Windows.
- **České hlášky** (`Msg.swift`, `PreflightCode`): texty Windows `Msg` pro kontroly, doplnění, místo, CLI; `Msg.bytes`
  (= `Format.BytesCs`), `Msg.count` (N0 cs-CZ s pevnou mezerou). **Engine je zatím nepoužívá** (anglické hlášky beze změny).
- **Odhad místa** (`Space.swift`, port `SpaceEstimator`/`SpacePlanner`): vzorce §2.5, po svazcích (`st_dev`), rezerva 5 %.
  **Zatím nezapojeno** do engine/jobu/GUI.
- `OffloadReport`: volitelné `generation`, `files[].md5` (vynechané, když nil).

## Nehotovo (pro další práci)

Engine (české hlášky + kódy, MD5 při kopii, `_REPORT` hned na začátku, přejmenování bez přepisu, režim doplnění),
`Report.write` (`_NNNN` jména, pořadí zápisu, prodloužení `.xxh64`/`.md5`), CardRecord + AppendPlanner + zámek složky
(flock), historie (`history.json`, otisk, gzip seznam, matcher, texty), ReelNames/název karty, BackupVerifier + report
ověření + nové `offload verify`, `offload append`, `--hash`/`--no-history`/`--name auto`, GUI (řádek historie, dotaz
„Karty už jsou zálohované“, doplnění, místo, menu Ověřit zálohu…), jména karet `_REPORT`/`_PROXY`/`ascmhl` → `_`.

## Zjištění a odchylky

- **Klíč karty na Macu:** sériové číslo FAT/exFAT Mac bez přístupu k `/dev/rdisk` (root) nepřečte. Ověřeno na obrazech
  disku: UUID svazku exFAT = UUID v3 = MD5(kFSUUIDNamespaceSHA1 + sériové číslo LE) (`6AC78196` → `02B3C48D-…-338F-…`);
  u FAT32 jde do MD5 ještě něco dalšího. Převod UUID → sériové číslo nejde. Návrh: na Macu `card.fsSerial` = UUID svazku
  (`volumeUUIDString`), klíč = UUID + kapacita; historie mezi Mac a Windows tedy přenosná není (obě jsou místní, falešná
  shoda nehrozí).
- `Msg.verifyHint` na Macu „Nástroje → Ověřit zálohu… (v příkazové řádce offload verify)“ (Windows jmenuje horní lištu
  nového vzhledu, který Mac nemá); cesty v hláškách s `/` místo `\`.
- MD5/SHA-1 přes CommonCrypto přímo ve Swiftu (varování o zastaralosti — jde o kontrolní součet, ne o bezpečnost).

## Ruční kontroly (čekají)

`ascmhl verify` (referenční ascmhl 1.2 je v pip venv ve scratchpadu) nad složkou s generací 2 z Macu; GUI nic.

## Merge hotspoty

`Report.swift` (nová pole v `OffloadReport`), `Hash.swift` (přepsaný, nové `hashFile` přetížení), `MHL.swift`
(`MHL` je teď `public`), `IO.swift` (přidané `writeFileNew`, `renameNoReplace`), nové `Space.swift` — kdyby proud P
přidal vlastní `SpaceEstimator` (pro `skipLighterSources`), sloučit do tohoto (`proxyVideoMbps` už tu je).
