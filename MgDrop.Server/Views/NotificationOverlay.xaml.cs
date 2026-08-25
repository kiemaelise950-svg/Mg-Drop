// ============================================================================
// MgDrop.Server — Views/NotificationOverlay.xaml.cs
// Code-behind de l'overlay de notification glassmorphique.
//
// ► Positionne la fenêtre en bas à droite (au-dessus de la barre des tâches).
// ► Animations Fade-In + Slide-Up à l'apparition.
// ► Animations Fade-Out + Slide-Down à la disparition.
// ► Auto-dismiss après 8 secondes.
// ► Bouton "Ouvrir" lance Process.Start sur le fichier reçu.
// ============================================================================

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace MgDrop.Server.Views;

public partial class NotificationOverlay : Window
{
    // ── Champs privés ──────────────────────────────────────────────────────
    private string _currentFilePath = string.Empty;
    private readonly DispatcherTimer _autoDismissTimer;

    // ── Constantes d'animation ─────────────────────────────────────────────
    private static readonly Duration FadeInDuration = new(TimeSpan.FromMilliseconds(400));
    private static readonly Duration FadeOutDuration = new(TimeSpan.FromMilliseconds(300));
    private const double SlideDistance = 20.0;
    private const int AutoDismissSeconds = 8;

    // ── Easing (courbe cubique fluide identique au CSS du Stitch) ──────────
    // cubic-bezier(0.16, 1, 0.3, 1) ≈ QuarticEase EaseOut
    private static readonly IEasingFunction SlideInEasing = new QuarticEase { EasingMode = EasingMode.EaseOut };
    private static readonly IEasingFunction SlideOutEasing = new QuadraticEase { EasingMode = EasingMode.EaseIn };

    // ════════════════════════════════════════════════════════════════════════
    // CONSTRUCTEUR
    // ════════════════════════════════════════════════════════════════════════

    public NotificationOverlay()
    {
        InitializeComponent();

        // Timer d'auto-dismiss
        _autoDismissTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(AutoDismissSeconds)
        };
        _autoDismissTimer.Tick += (_, _) =>
        {
            _autoDismissTimer.Stop();
            HideOverlay();
        };
    }

    // ════════════════════════════════════════════════════════════════════════
    // POSITIONNEMENT — Bas à droite, au-dessus de la barre des tâches
    // ════════════════════════════════════════════════════════════════════════

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        PositionBottomRight();

        // Fenêtre cachée au démarrage (mode headless)
        Opacity = 0;
        Visibility = Visibility.Hidden;
    }

    /// <summary>
    /// Calcule et applique la position bas-droite en utilisant
    /// SystemParameters.WorkArea (exclut la barre des tâches).
    /// </summary>
    private void PositionBottomRight()
    {
        var workArea = SystemParameters.WorkArea;

        // Position : coin bas-droit avec une marge de 12px
        Left = workArea.Right - ActualWidth - 12;
        Top = workArea.Bottom - ActualHeight - 12;
    }

    // ════════════════════════════════════════════════════════════════════════
    // API PUBLIQUE — Déclenchement de la notification
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Point d'entrée principal appelé par le module TCP (NetworkReceiverService)
    /// via App.xaml.cs lorsqu'un fichier est reçu avec succès.
    /// </summary>
    /// <param name="fileName">Nom du fichier reçu.</param>
    /// <param name="filePath">Chemin complet du fichier sauvegardé.</param>
    /// <param name="fileSize">Taille formatée (ex: "12.4 Mo").</param>
    public void TriggerFileReceivedNotification(string fileName, string filePath, string fileSize)
    {
        Debug.WriteLine($"[MG Drop UI] 📬 Notification déclenchée pour : {fileName}");

        _currentFilePath = filePath;

        // ── Mise à jour des textes ────────────────────────────────────────
        FileDescriptionText.Text = "Fichier reçu depuis le smartphone";
        FileNameText.Text = $"{fileName} — {fileSize}";
        TimestampText.Text = "À l'instant";

        // ── Détection de l'icône selon l'extension ────────────────────────
        try
        {
            var sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(filePath);
            if (sysIcon != null)
            {
                FileThumbnail.Source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                    sysIcon.Handle,
                    Int32Rect.Empty,
                    System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                FileIconText.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MG Drop UI] ❌ Erreur extraction icône : {ex.Message}");
            FileIconText.Visibility = Visibility.Visible;
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            FileIconText.Text = extension switch
            {
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" => "🖼️",
                ".mp4" or ".avi" or ".mkv" or ".mov" or ".wmv" => "🎬",
                ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg" => "🎵",
                ".pdf" => "📕",
                ".doc" or ".docx" => "📝",
                ".xls" or ".xlsx" => "📊",
                ".zip" or ".rar" or ".7z" or ".tar" => "📦",
                ".apk" => "📱",
                _ => "📄"
            };
        }

        // ── Barre de progression à 100% (transfert déjà terminé) ──────────
        ProgressBar.Width = ((Border)ProgressBar.Parent).ActualWidth > 0
            ? ((Border)ProgressBar.Parent).ActualWidth
            : 340; // Fallback

        // ── Recalcul de la position et affichage ──────────────────────────
        UpdateLayout();
        PositionBottomRight();
        ShowOverlay();
    }

    // ════════════════════════════════════════════════════════════════════════
    // ANIMATIONS
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Animation d'entrée : Fade-In (0→1) + Slide-Up (20px→0).
    /// Reproduit le CSS : animation: slideIn 0.4s cubic-bezier(0.16, 1, 0.3, 1)
    /// </summary>
    private void ShowOverlay()
    {
        // Arrête le timer précédent si une notification est déjà visible
        _autoDismissTimer.Stop();

        // Rend la fenêtre visible
        Visibility = Visibility.Visible;

        // ── Animation d'opacité (Fade-In) ─────────────────────────────────
        var fadeIn = new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = FadeInDuration,
            EasingFunction = SlideInEasing
        };

        // ── Animation de translation verticale (Slide-Up) ─────────────────
        var slideUp = new DoubleAnimation
        {
            From = SlideDistance,
            To = 0.0,
            Duration = FadeInDuration,
            EasingFunction = SlideInEasing
        };

        // Lancement des animations
        BeginAnimation(OpacityProperty, fadeIn);
        SlideTransform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, slideUp);

        // Démarrage du timer d'auto-dismiss
        _autoDismissTimer.Start();

        Debug.WriteLine("[MG Drop UI] ✨ Overlay affiché (Fade-In + Slide-Up)");
    }

    /// <summary>
    /// Animation de sortie : Fade-Out (1→0) + Slide-Down (0→20px).
    /// Cache la fenêtre à la fin de l'animation.
    /// </summary>
    private void HideOverlay()
    {
        _autoDismissTimer.Stop();

        // ── Animation d'opacité (Fade-Out) ────────────────────────────────
        var fadeOut = new DoubleAnimation
        {
            From = 1.0,
            To = 0.0,
            Duration = FadeOutDuration,
            EasingFunction = SlideOutEasing
        };

        // ── Animation de translation verticale (Slide-Down) ───────────────
        var slideDown = new DoubleAnimation
        {
            From = 0.0,
            To = SlideDistance,
            Duration = FadeOutDuration,
            EasingFunction = SlideOutEasing
        };

        // Cache la fenêtre à la fin du Fade-Out
        fadeOut.Completed += (_, _) =>
        {
            Visibility = Visibility.Hidden;
            Debug.WriteLine("[MG Drop UI] 🫥 Overlay masqué (Fade-Out + Slide-Down)");
        };

        // Lancement des animations
        BeginAnimation(OpacityProperty, fadeOut);
        SlideTransform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, slideDown);
    }

    // ════════════════════════════════════════════════════════════════════════
    // EVENT HANDLERS — Boutons d'action
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Bouton "Ouvrir" : ouvre le fichier reçu avec l'application par défaut,
    /// ou le dossier Téléchargements si le fichier n'existe plus.
    /// </summary>
    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (File.Exists(_currentFilePath))
            {
                // Ouvre le fichier avec l'application associée par défaut
                Process.Start(new ProcessStartInfo
                {
                    FileName = _currentFilePath,
                    UseShellExecute = true
                });
                Debug.WriteLine($"[MG Drop UI] 📂 Ouverture du fichier : {_currentFilePath}");
            }
            else
            {
                // Fallback : ouvre le dossier Téléchargements
                var downloadsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                Process.Start("explorer.exe", downloadsPath);
                Debug.WriteLine("[MG Drop UI] 📂 Fichier introuvable, ouverture du dossier Téléchargements");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MG Drop UI] ❌ Erreur lors de l'ouverture : {ex.Message}");
        }

        HideOverlay();
    }

    /// <summary>
    /// Bouton "Fermer" : lance l'animation de sortie.
    /// </summary>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        HideOverlay();
    }

    // ── Protection contre la fermeture accidentelle ────────────────────────
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Empêche la fermeture de la fenêtre (on la cache simplement)
        // L'application tourne en System Tray
        e.Cancel = true;
        HideOverlay();
    }
}
