// ============================================================================
// MgDrop.Server — App.xaml.cs
// Code-behind de l'application WPF.
//
// ► Démarre les services TCP et NFC en arrière-plan.
// ► Place l'application dans le System Tray (NotifyIcon).
// ► Connecte les événements réseau à l'overlay de notification.
// ============================================================================

using System.Diagnostics;
using System.IO;
using System.Windows;
using MgDrop.Server.Services;

namespace MgDrop.Server;

public partial class App : System.Windows.Application
{
    // ── Services backend (Sprint 1) ────────────────────────────────────────
    private NetworkReceiverService? _networkReceiver;
    private NfcBroadcasterService? _nfcBroadcaster;
    private CancellationTokenSource? _cts;

    // ── System Tray ────────────────────────────────────────────────────────
    private System.Windows.Forms.NotifyIcon? _trayIcon;

    private void App_OnStartup(object sender, StartupEventArgs e)
    {
        Debug.WriteLine("[MG Drop] 🚀 Démarrage de l'application WPF...");

        // ── Initialisation des services ───────────────────────────────────
        _cts = new CancellationTokenSource();
        _networkReceiver = new NetworkReceiverService();
        _nfcBroadcaster = new NfcBroadcasterService();

        // ── Abonnement aux événements de réception ────────────────────────
        _networkReceiver.FileReceived += (sender, filePath) =>
        {
            var fileInfo = new FileInfo(filePath);
            var sizeText = FormatSize(fileInfo.Length);
            var fileName = fileInfo.Name;

            Debug.WriteLine($"[MG Drop] 🎉 Fichier reçu et écrit sur le disque : {fileName} ({sizeText})");

            // Déclenche la notification overlay sur le thread UI
            Dispatcher.Invoke(() =>
            {
                if (MainWindow is Views.NotificationOverlay overlay)
                {
                    overlay.TriggerFileReceivedNotification(fileName, filePath, sizeText);
                }
            });
        };

        // ── Démarrage du serveur TCP en arrière-plan ──────────────────────
        _ = Task.Run(async () =>
        {
            try
            {
                await _networkReceiver.StartAsync(_cts.Token);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MG Drop] 💀 Erreur fatale TCP : {ex.Message}");
            }
        });

        // Petite pause pour laisser le serveur TCP se bind
        Task.Delay(500).Wait();

        // ── Initialisation du NFC ─────────────────────────────────────────
        var nfcAvailable = _nfcBroadcaster.Initialize();
        if (nfcAvailable)
        {
            _nfcBroadcaster.StartBroadcasting(
                _networkReceiver.LocalIpAddress,
                _networkReceiver.ListeningPort);
        }

        Debug.WriteLine($"[MG Drop] 🌐 Serveur sur {_networkReceiver.LocalIpAddress}:{_networkReceiver.ListeningPort}");
        Debug.WriteLine($"[MG Drop] 📡 NFC : {(nfcAvailable ? "ACTIF" : "NON DISPONIBLE")}");

        // ── Configuration du System Tray ──────────────────────────────────
        SetupSystemTray();
    }

    /// <summary>
    /// Configure l'icône dans la zone de notification Windows (System Tray).
    /// </summary>
    private void SetupSystemTray()
    {
        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            // Icône Information (un "i" bleu, garanti d'être visible)
            Icon = System.Drawing.SystemIcons.Information,
            Text = $"MG Drop — {_networkReceiver?.LocalIpAddress}:{_networkReceiver?.ListeningPort}",
            Visible = true
        };

        // Menu contextuel du tray
        var contextMenu = new System.Windows.Forms.ContextMenuStrip();

        var statusItem = contextMenu.Items.Add(
            $"🌐 IP : {_networkReceiver?.LocalIpAddress}:{_networkReceiver?.ListeningPort}");
        statusItem.Enabled = false;

        var nfcStatus = _nfcBroadcaster != null && _nfcBroadcaster.IsNfcAvailable 
            ? "ACTIF (Prêt pour le contact)" 
            : "NON DÉTECTÉ (Vérifiez votre matériel)";
        
        var nfcItem = contextMenu.Items.Add($"📡 NFC : {nfcStatus}");
        nfcItem.Enabled = false;

        contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        var openDownloadsItem = contextMenu.Items.Add("📂 Ouvrir Téléchargements");
        openDownloadsItem.Click += (_, _) =>
        {
            var downloadsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            Process.Start("explorer.exe", downloadsPath);
        };

        contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        var quitItem = contextMenu.Items.Add("❌ Quitter MG Drop");
        quitItem.Click += (_, _) => Shutdown();

        _trayIcon.ContextMenuStrip = contextMenu;

        // Double-clic sur l'icône tray = Ouvre le dossier Téléchargements
        _trayIcon.DoubleClick += (_, _) =>
        {
            var downloadsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            Process.Start("explorer.exe", downloadsPath);
        };
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} octets",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} Ko",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):F1} Mo",
        _ => $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} Go"
    };

    private void App_OnExit(object sender, ExitEventArgs e)
    {
        Debug.WriteLine("[MG Drop] 👋 Arrêt de l'application...");

        _cts?.Cancel();
        _nfcBroadcaster?.Dispose();
        _networkReceiver?.Dispose();

        _trayIcon?.Dispose();
    }
}
