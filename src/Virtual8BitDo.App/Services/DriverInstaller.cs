using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace Virtual8BitDo.App.Services;

/// <summary>
/// Instala um driver necessário (ViGEmBus ou HidHide) direto da fonte oficial, dando crédito aos autores:
///  1) pergunta ao GitHub qual é a versão mais recente publicada pela Nefarius;
///  2) baixa o instalador oficial;
///  3) confere a assinatura digital (tem que ser válida e da "Nefarius Software Solutions");
///  4) abre o instalador (o Windows pede permissão de administrador; o usuário conclui).
/// </summary>
public static class DriverInstaller
{
    public sealed record Driver(string Name, string Repo, string AssetSuffix, string Url);

    public static readonly Driver ViGEmBus = new("ViGEmBus", "nefarius/ViGEmBus", ".exe", "https://github.com/nefarius/ViGEmBus");
    public static readonly Driver HidHide = new("HidHide", "nefarius/HidHide", "_x64.exe", "https://github.com/nefarius/HidHide");

    private const string TrustedSigner = "Nefarius Software Solutions";

    /// <summary>Retorna null se deu certo, ou a mensagem de erro.</summary>
    public static async Task<string?> InstallAsync(Driver driver, IProgress<string> progress)
    {
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Virtual8BitDo");

            progress.Report($"Procurando a versão mais recente do {driver.Name}...");
            using var release = JsonDocument.Parse(await http.GetStringAsync($"https://api.github.com/repos/{driver.Repo}/releases/latest"));
            var asset = release.RootElement.GetProperty("assets").EnumerateArray()
                .FirstOrDefault(a => a.GetProperty("name").GetString()!.EndsWith(driver.AssetSuffix, StringComparison.OrdinalIgnoreCase));
            if (asset.ValueKind == JsonValueKind.Undefined) return $"Instalador do {driver.Name} não encontrado no GitHub.";

            string name = asset.GetProperty("name").GetString()!;
            string folder = Path.Combine(Path.GetTempPath(), "Virtual8BitDo", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, name);

            progress.Report($"Baixando {name}...");
            await File.WriteAllBytesAsync(file, await http.GetByteArrayAsync(asset.GetProperty("browser_download_url").GetString()));

            progress.Report("Conferindo a assinatura digital...");
            if (VerifySignature(file) is string signatureError) return signatureError;

            progress.Report($"Abrindo o instalador do {driver.Name}. Conclua a instalação na janela que abrir.");
            using var installer = Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
            if (installer != null) await installer.WaitForExitAsync();
            return null;
        }
        catch (System.ComponentModel.Win32Exception) { return "Instalação cancelada."; } // permissão de administrador negada
        catch (Exception ex) { return $"Não foi possível instalar o {driver.Name}: {ex.Message}"; }
    }

    /// <summary>Null se a assinatura é válida e do autor esperado; senão, o motivo.</summary>
    private static string? VerifySignature(string file)
    {
        if (WinVerifyTrustFile(file) != 0) return "A assinatura digital do instalador é inválida. Instalação cancelada por segurança.";
        try
        {
            // Lê o certificado de quem assinou o .exe. O X509CertificateLoader sugerido pelo aviso não lê
            // assinaturas Authenticode de executáveis; a validade em si já foi checada pelo WinVerifyTrust acima.
#pragma warning disable SYSLIB0057
            var signer = X509Certificate.CreateFromSignedFile(file).Subject;
#pragma warning restore SYSLIB0057
            return signer.Contains(TrustedSigner, StringComparison.OrdinalIgnoreCase)
                ? null
                : $"O instalador foi assinado por outra pessoa ({signer}). Instalação cancelada por segurança.";
        }
        catch (Exception) { return "O instalador não tem assinatura digital. Instalação cancelada por segurança."; }
    }

    // ---- WinVerifyTrust: a mesma checagem que o Windows faz em arquivos assinados ----
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData, pSIPClientData;
        public uint dwUIChoice, fdwRevocationChecks, dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData, pwszURLReference;
        public uint dwProvFlags, dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WINTRUST_DATA data);

    private static int WinVerifyTrustFile(string path)
    {
        var fileInfo = new WINTRUST_FILE_INFO { cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(), pcwszFilePath = path };
        IntPtr pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        try
        {
            Marshal.StructureToPtr(fileInfo, pFile, false);
            var data = new WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                dwUIChoice = 2,          // WTD_UI_NONE
                fdwRevocationChecks = 0, // WTD_REVOKE_NONE
                dwUnionChoice = 1,       // WTD_CHOICE_FILE
                pFile = pFile,
                dwStateAction = 0,
                dwProvFlags = 0x80,      // WTD_CACHE_ONLY_URL_RETRIEVAL
            };
            var action = GenericVerifyV2;
            return WinVerifyTrust(IntPtr.Zero, ref action, ref data);
        }
        finally
        {
            Marshal.DestroyStructure<WINTRUST_FILE_INFO>(pFile);
            Marshal.FreeHGlobal(pFile);
        }
    }
}
