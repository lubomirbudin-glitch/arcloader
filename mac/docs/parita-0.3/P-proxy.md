# Stream P — proxy (Mac parita 0.3)

Větev `worktree-agent-acbfd248aeb877f96`, základ `48b483f` (main; worktree vznikl na starším `ef25348` bez testů —
převinuto fast-forward). Práce zkrácená termínem koordinátora: hotová je jen část funkce 9.

## Hotovo (commit `7ce73ca`)

- **Cívka klipu** (`ReelResolver`, nový `Sources/OffloadKit/Reel.swift`): tag kamery z ffprobe (`reel_name`, `tape_name`,
  `reel`, bez ohledu na velikost písmen; formát, pak proudy — MOV na stopě tmcd, MXF na každém proudu), jinak cívka
  z názvu klipu (vzory Windows `ReelNames`: Blackmagic, RED, ARRI/Sony Venice/Canon), jinak název složky karty.
  `ClipInfo.reel` → report `clips[].info.reel` (chybí, když je nil). Nastaví se po sondě i u kodeku bez dekodéru (X-OCN).
- **Proxy:** s platným TC navíc `-metadata:s:v:0 reel_name=<cívka>`; ffmpeg 9 ho zapíše do stopy tmcd (ověřeno ffprobe 9:
  `reel_name` na datovém proudu tmcd). Bez TC žádná tmcd ani cívka.
- **Všechny zvukové stopy:** `-map 0:a?` místo `-map 0:a:0?`; ProRes `pcm_s16le`, H.264/HEVC AAC.
- Testy `ReelTests` (4): pořadí tag → název → karta, čtení tagu z JSON ffprobe, render H.264 i ProRes Proxy se dvěma
  zvukovými stopami (cívka a TC na tmcd, 2× aac / 2× pcm_s16le), bez TC bez tmcd a cívky, report s cívkou celé úlohy.
  JSON ffprobe (`-show_streams`) video stopu nezdvojuje (dvakrát ji ukazuje jen výpis `-show_entries … -of compact/csv`
  v sekci `stream_group`).

## Odchylky

- **AAC jednotně `-b:a 192k` pro každou stopu** (Windows: 64 kb/s × kanály stopy, 128–512k, podle `ClipInfo.AudioStreams`).
  Chybí `ClipInfo.audioStreams` (kanály po stopách, mimo JSON) — vyžaduje nekódovanou vlastnost v `ClipInfo`; návrh:
  property wrapper bez klíče v JSON (ne `CodingKeys`, aby nové klíče jiných streamů, např. `cameraSerial`, zůstaly v JSON).
- `ReelResolver` má vlastní kopii vzorů názvů; stream A má `ReelNames` — **při slučování sjednotit**
  (`ReelResolver.fromClipName` → `ReelNames.fromClipName`). Kdyby stream A přenesl i `ReelResolver`, kompilace ohlásí
  dvojí definici — ponechat jednu.

## Nehotovo (z rozsahu P)

- Poznámka u přejmenované proxy „Proxy přejmenována na … (shoda jmen) — přelinkujte ji ručně.“
- 0.5.1 strom `_PROXY/<složka klipu>/` (`ProxyPaths`: folder/relative/pick/detect z reportů/existing), složka pro proxy
  navíc s podsložkami, `proxyPath` s podsložkou, finální přejmenování proxy bez přepsání (`renameNoReplace`).
- HTML: odkaz „hotovo“ na `../_PROXY/…`, sloupec Proxy bez dělení slov (`td.st`).
- `proxy.skipLighterSources` (`LighterSource`, `ClipInfo.videoBitrate`, „bez proxy — originál je lehčí (X Mb/s ≤ Y Mb/s)“,
  jen H.264/HEVC proxy, ne s LUTem ani ořezem), CLI `--proxy-always`, přepínač v GUI, souhrny.
- Zkrácené cesty `.part` (Windows MAX_PATH) — na macOS netřeba (PATH_MAX 1024).

## Čeká na ověření

- Přelinkování v Resolve (Reel) a Premiere (Tape Name) s proxy z Macu — ruční test.
- ROZDILY řádek „Proxy pro přelinkování“ může pro Mac přejít na „✅ cívka + všechny stopy (AAC 192k na stopu)“
  až po ručním testu; řádky „Proxy ve složkách“ a „Bez proxy, když je originál lehčí“ zůstávají ⏳.
- `ProxyFolderTests.testJobCopiesEveryProxyIntoTheFolder` trval při souběhu 4 agentů 1057 s: PDF z WebKitu
  („report did not load in 60 s“) — čekání trvalo mnohem déle než 60 s; prověřit `PDFReport` (nesouvisí se změnou P).

## Místa střetu při slučování

`Media.swift` (`ClipInfo` — nové pole `reel` vedle polí streamu C), `Job.swift` (smyčka analýzy a proxy — stream A
doplnění), `Proxy.swift` `renderOnce` (zvuk, timecode).
