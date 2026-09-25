using System.Text;

namespace InstallerBuilder.Core;

/// <summary>
/// Az autounattend.xml "nem sima szöveges" jelszó-formátuma: Base64( UTF-16LE( jelszó + mezőnév ) ), ahol a mezőnév
/// az AutoLogon és a LocalAccount jelszavánál "Password". Ez NEM titkosítás, csak eltakarás - aki a pendrive-ot
/// megkapja, visszafejtheti. A Windows a telepítés után a lemezre mentett unattend.xml-ben ezeket a mezőket törli.
/// </summary>
public static class PasswordEncoder
{
    public static string Encode(string password, string fieldName = "Password") =>
        Convert.ToBase64String(Encoding.Unicode.GetBytes(password + fieldName));
}
