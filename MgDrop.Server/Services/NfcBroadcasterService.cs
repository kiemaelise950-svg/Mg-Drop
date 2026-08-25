// ============================================================================
// MgDrop.Server — Services/NfcBroadcasterService.cs
// Module 2 : Broadcaster NFC via l'API Windows.Networking.Proximity.
//
// ► Vérifie la présence du matériel NFC.
// ► Publie en boucle un message NDEF contenant l'adresse IP:Port du serveur TCP.
// ► Permet au smartphone Android de lire le tag d'un simple contact physique.
// ============================================================================

using Windows.Networking.Proximity;

namespace MgDrop.Server.Services;

/// <summary>
/// Service de diffusion NFC qui publie les coordonnées réseau du serveur
/// (IP:Port) via l'antenne NFC intégrée du PC sous forme de message NDEF.
/// </summary>
public sealed class NfcBroadcasterService : IDisposable
{
    // ── Champs privés ──────────────────────────────────────────────────────
    private ProximityDevice? _proximityDevice;
    private long _publishedMessageId = -1;
    private bool _isPublishing;

    // ── Propriétés publiques ───────────────────────────────────────────────
    /// <summary>Indique si le matériel NFC est disponible et activé.</summary>
    public bool IsNfcAvailable { get; private set; }

    /// <summary>Indique si un message est actuellement publié via NFC.</summary>
    public bool IsPublishing => _isPublishing;

    // ── Événements ─────────────────────────────────────────────────────────
    /// <summary>Déclenché lorsqu'un appareil entre en proximité NFC.</summary>
    public event Action<string>? OnDeviceArrived;

    /// <summary>Déclenché lorsqu'un appareil quitte la zone NFC.</summary>
    public event Action? OnDeviceDeparted;

    // ════════════════════════════════════════════════════════════════════════
    // INITIALISATION — Détection du matériel NFC
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Initialise le service NFC en vérifiant la présence du matériel.
    /// Doit être appelé avant <see cref="StartBroadcasting"/>.
    /// </summary>
    /// <returns>true si le NFC est disponible, false sinon.</returns>
    public bool Initialize()
    {
        try
        {
            Console.WriteLine("[MG Drop NFC] 🔍 Recherche du module NFC...");

            _proximityDevice = ProximityDevice.GetDefault();

            if (_proximityDevice is null)
            {
                Console.WriteLine("[MG Drop NFC] ❌ Aucun module NFC détecté sur cette machine.");
                Console.WriteLine("[MG Drop NFC]    → Le transfert par contact physique ne sera pas disponible.");
                Console.WriteLine("[MG Drop NFC]    → Le smartphone devra se connecter manuellement via l'IP affichée.");
                IsNfcAvailable = false;
                return false;
            }

            Console.WriteLine($"[MG Drop NFC] ✅ Module NFC détecté : {_proximityDevice.DeviceId}");
            Console.WriteLine($"[MG Drop NFC]    → Capacité maximale du message : {_proximityDevice.MaxMessageBytes} octets");

            // Écoute des événements de proximité (arrivée/départ d'appareil)
            _proximityDevice.DeviceArrived += OnProximityDeviceArrived;
            _proximityDevice.DeviceDeparted += OnProximityDeviceDeparted;

            IsNfcAvailable = true;
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MG Drop NFC] ❌ Erreur lors de l'initialisation NFC : {ex.Message}");
            IsNfcAvailable = false;
            return false;
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // PUBLICATION — Diffusion du message NDEF
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Publie les coordonnées réseau du serveur TCP via NFC.
    /// Le message est un record NDEF de type URI contenant "mgdrop://IP:PORT".
    /// Le message reste publié en continu jusqu'à l'appel de <see cref="StopBroadcasting"/>.
    /// </summary>
    /// <param name="ipAddress">L'adresse IPv4 locale du serveur.</param>
    /// <param name="port">Le port TCP sur lequel le serveur écoute.</param>
    public void StartBroadcasting(string ipAddress, int port)
    {
        if (!IsNfcAvailable || _proximityDevice is null)
        {
            Console.WriteLine("[MG Drop NFC] ⚠️  Impossible de diffuser : NFC non disponible.");
            return;
        }

        if (_isPublishing)
        {
            Console.WriteLine("[MG Drop NFC] ⚠️  Un message est déjà en cours de diffusion. Arrêt de l'ancien...");
            StopBroadcasting();
        }

        try
        {
            // Format du message NDEF : URI personnalisée "mgdrop://IP:PORT"
            // Ce format permet au client Flutter de l'identifier facilement
            // via un filtre d'intent NFC Android.
            var ndefPayload = $"mgdrop://{ipAddress}:{port}";

            Console.WriteLine($"[MG Drop NFC] 📡 Publication du tag NDEF...");
            Console.WriteLine($"[MG Drop NFC]    → Contenu : \"{ndefPayload}\"");

            // Publication NDEF de type URI — le message reste actif
            // et sera lu par tout appareil NFC qui entre en contact.
            _publishedMessageId = _proximityDevice.PublishUriMessage(
                new Uri(ndefPayload));

            _isPublishing = true;

            Console.WriteLine($"[MG Drop NFC] ✅ Tag NFC publié avec succès (ID message : {_publishedMessageId})");
            Console.WriteLine("[MG Drop NFC] 📲 En attente d'un contact NFC avec un smartphone...");
            Console.WriteLine();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MG Drop NFC] ❌ Erreur lors de la publication NFC : {ex.Message}");
            _isPublishing = false;
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // ARRÊT — Suppression du message NFC
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Arrête la diffusion du message NDEF.</summary>
    public void StopBroadcasting()
    {
        if (!_isPublishing || _proximityDevice is null) return;

        try
        {
            _proximityDevice.StopPublishingMessage(_publishedMessageId);
            _isPublishing = false;
            _publishedMessageId = -1;
            Console.WriteLine("[MG Drop NFC] 🛑 Diffusion NFC arrêtée.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MG Drop NFC] ⚠️  Erreur lors de l'arrêt de la diffusion : {ex.Message}");
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // CALLBACKS — Événements de proximité
    // ════════════════════════════════════════════════════════════════════════

    private void OnProximityDeviceArrived(ProximityDevice sender)
    {
        var deviceInfo = $"Appareil NFC détecté (ID: {sender.DeviceId})";
        Console.WriteLine($"[MG Drop NFC] 📲 {deviceInfo}");
        Console.WriteLine("[MG Drop NFC]    → Le smartphone lit les coordonnées du serveur...");
        OnDeviceArrived?.Invoke(deviceInfo);
    }

    private void OnProximityDeviceDeparted(ProximityDevice sender)
    {
        Console.WriteLine("[MG Drop NFC] 👋 L'appareil NFC a quitté la zone de proximité.");
        OnDeviceDeparted?.Invoke();
    }

    // ── IDisposable ────────────────────────────────────────────────────────

    public void Dispose()
    {
        StopBroadcasting();

        if (_proximityDevice is not null)
        {
            _proximityDevice.DeviceArrived -= OnProximityDeviceArrived;
            _proximityDevice.DeviceDeparted -= OnProximityDeviceDeparted;
        }
    }
}
