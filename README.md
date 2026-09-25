# Windows Telepítő Készítő

Saját Windows telepítő pendrive vagy ISO, amely a Windows telepítése után **a kiválasztott programokat is feltelepíti**.
Az első bejelentkezéskor egy pipálható lista jelenik meg a programjaiddal, és te döntöd el, melyik települjön.

- **Friss Windows:** a legújabb Windows 11 (vagy 10) letöltése közvetlenül a Microsoft hivatalos szerveréről, magyar
  nyelven. Letöltött ISO-ból vagy egy már kész telepítő pendrive-ból is dolgozik.
- **Programok:** a [winget](https://learn.microsoft.com/windows/package-manager/) katalógusából (mindig a legfrissebb
  verzió) és saját telepítőkből (`.exe`, `.msi`, `.msix`, `.reg`, `.cmd`, `.ps1`). A saját telepítők a pendrive-ra
  kerülnek, így internet nélkül is feltelepülnek.
- **Automatikus beállítás:** magyar nyelv, billentyűzet és időzóna, helyi felhasználó (Microsoft-fiók nélkül), a kezdeti
  kérdések átugrása, számítógépnév.
- **Kimenet:** bootolható pendrive a nulláról (UEFI és régi BIOS), új ISO fájl, vagy csak a kiegészítő fájlok egy mappába.

## Letöltés

A kész program a GitHub **Actions** fülén (a legutóbbi sikeres futás „WindowsTelepitoKeszito” mellékletében), illetve
a **Releases** oldalon található egyetlen `WindowsTelepitoKeszito.exe` fájlként. Telepíteni nem kell, és .NET sem
kell hozzá. Rendszergazdai jogot kér, mert pendrive-ot formáz és ISO-t csatol.

## Használat

1. **Programok fül:** add hozzá a programokat. Kereshetsz a winget katalógusban, választhatsz a népszerűek közül, vagy
   megadhatsz saját telepítőt. A pipa jelenti, hogy a telepítéskor alapból be legyen-e jelölve. Az **Azonosítók
   ellenőrzése** gomb megnézi, hogy minden winget-azonosító létezik-e.
2. **Windows beállítások fül:** nyelv és időzóna, a felhasználó neve és jelszava, a gép neve, a programválasztó
   visszaszámlálása (0 = megvárja, hogy kattints).
3. **Készítés fül:**
   - *Friss letöltés* + *Bootolható pendrive*: letölti a Windows-t, formázza a pendrive-ot, és elkészíti a telepítőt.
     A formázás előtt kétszer rákérdez. Csak USB-s meghajtót enged kiválasztani, a rendszerlemezt soha.
   - *Meglévő ISO* + *Új ISO*: egy Windows ISO-ból új ISO-t készít. Ehhez a
     [Windows ADK](https://learn.microsoft.com/windows-hardware/get-started/adk-install) „Deployment Tools” része kell.
   - *Kész telepítő pendrive*: egy Media Creation Tool-lal vagy Rufus-szal készült pendrive-ra csak a kiegészítő fájlokat
     írja rá. A rajta lévő Windows megmarad, egy meglévő `autounattend.xml`-ről pedig biztonsági másolat készül.
4. **A profil** (a programlista és a beállítások) menthető, így a következő géphez egy kattintással újra elkészíthető.

## Mi történik telepítéskor?

1. A gép a pendrive-ról indul. A telepítő a **lemezt és a Windows kiadását (termékkulcsot) továbbra is megkérdezi**.
   Ezt szándékosan nem automatizáltuk, mert egy rossz lemez letörlése visszafordíthatatlan.
2. A nyelv, a régió és a kezdeti kérdések automatikusan kitöltődnek, és létrejön a helyi felhasználó.
   A WiFi-oldal látszik, mert a winget-es programokhoz internet kell.
3. Az első (automatikus) bejelentkezéskor megjelenik a **programválasztó**. Utána a telepítés a háttérben fut, közben
   már használhatod a gépet. A folyamat-ablak mutatja, melyik program sikerült és melyik nem.
4. A napló helye: `C:\Windows\Setup\Scripts\ProgramTelepito\telepites-naplo.txt`. Hiba esetén az asztalra is kikerül.
   Ha egy program kimaradt, a `Programok-telepitese.cmd` (ugyanitt) újraindítja a listát.

## Jó tudni

- **Jelszó:** a pendrive-on visszafejthető formában van (ez a Windows automatikus telepítésének sajátja). A telepítés
  közben törlődik a gépről, de a pendrive-ot ne add oda másnak, vagy utána változtasd meg a jelszót.
- **Automatikus letöltés:** a Microsoft néha nem engedi (sok próbálkozás után, vagy VPN-ről). Ilyenkor a program
  megnyitja a Microsoft letöltőoldalát. Töltsd le kézzel az ISO-t, és válaszd a *Meglévő ISO fájl* lehetőséget.
- **Saját telepítők:** a csendes telepítéshez kapcsoló kell. A program felismeri a gyakori telepítőket (Inno Setup,
  NSIS, InstallShield, WiX), és javasol hozzá kapcsolót. Kapcsoló nélkül a telepítő a saját ablakával indul.
- **FAT32:** a pendrive FAT32, hogy minden gépen bootoljon. A 4 GB-nál nagyobb Windows képfájlt a program
  automatikusan feldarabolja. A saját telepítők közül a 4 GB-nál nagyobb nem fér el (ISO-ba igen).
- **Architektúra:** x64 (a legtöbb PC). ARM-os gépekhez (pl. Snapdragon laptopok) nem készít telepítőt.

## Fejlesztés

```
dotnet test tests/InstallerBuilder.Core.Tests          # C# tesztek (Linuxon is futnak)
pwsh tests/Install-Programs.Tests.ps1                   # a telepítéskor futó szkript tesztje (próbaüzem)
dotnet publish src/InstallerBuilder.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

| Mappa | Tartalom |
|---|---|
| `src/InstallerBuilder.Core` | autounattend.xml generátor, adathordozó-terv és -író, Microsoft ISO letöltő, winget-kimenet feldolgozó, a telepítéskor futó szkriptek (`Resources/`) |
| `src/InstallerBuilder.App` | a Windows program (WinForms): fülek, a készítés lépései (diskpart, DISM, bootsect, oscdimg) |
| `tests/` | C# egységtesztek, a szkriptek próbaüzemű és felület-tesztjei |

A GitHub Actions minden változásnál lefordítja és teszteli a programot. Egy `v*` címke (pl. `v1.0.0`) feltöltésekor
kiadást is készít a kész `.exe`-vel.
