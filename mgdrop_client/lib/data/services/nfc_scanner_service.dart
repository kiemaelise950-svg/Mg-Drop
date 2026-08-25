import 'dart:async';
import 'dart:convert';
import 'package:flutter/foundation.dart';
import 'package:nfc_manager/nfc_manager.dart';
import 'package:vibration/vibration.dart';

/// Exception personnalisée pour la gestion des erreurs NFC.
class NfcException implements Exception {
  final String message;
  NfcException(this.message);

  @override
  String toString() => 'NfcException: $message';
}

/// Modèle pour encapsuler les informations de connexion reçues via NFC.
class ServerConnectionInfo {
  final String ipAddress;
  final int port;

  ServerConnectionInfo({required this.ipAddress, required this.port});
}

/// Service dédié à la gestion du NFC.
/// 
/// Rôle :
/// - Vérifier la disponibilité du NFC.
/// - Scanner un tag NDEF.
/// - Décoder l'adresse IP et le port (ex: "mgdrop://192.168.1.15:8080").
/// - Déclencher un retour haptique (vibration) au succès.
class NfcScannerService {
  /// Vérifie si le capteur NFC est disponible sur l'appareil.
  Future<bool> isNfcAvailable() async {
    try {
      final availability = await NfcManager.instance.checkAvailability();
      debugPrint('[NfcScannerService] NFC disponible : $availability');
      return availability.toString().contains('available') || availability.name == 'available';
    } catch (e) {
      debugPrint('[NfcScannerService] Erreur lors de la vérification NFC : $e');
      return false;
    }
  }

  /// Lance une session NFC et attend la lecture d'un tag NDEF.
  /// 
  /// Retourne un [ServerConnectionInfo] ou throw une [NfcException].
  Future<ServerConnectionInfo> scanForPcConnection() async {
    if (!await isNfcAvailable()) {
      throw NfcException("Le module NFC n'est pas activé ou disponible sur ce téléphone.");
    }

    debugPrint('[NfcScannerService] Scan NFC actif...');
    final completer = Completer<ServerConnectionInfo>();

    try {
      await NfcManager.instance.startSession(
        pollingOptions: {NfcPollingOption.iso14443, NfcPollingOption.iso15693},
        onDiscovered: (NfcTag tag) async {
          debugPrint('[NfcScannerService] Tag détecté ! Traitement en cours...');
          
          try {
            // ignore: invalid_use_of_protected_member
            final mapData = tag.data as Map<String, dynamic>;
            final ndefData = mapData['ndef'];
            
            if (ndefData == null) {
              debugPrint('[NfcScannerService] Le tag n\'est pas au format NDEF valide.');
              NfcManager.instance.stopSession();
              completer.completeError(NfcException("Le tag n'est pas au format NDEF attendu."));
              return;
            }

            final cachedMessage = ndefData['cachedMessage'];
            if (cachedMessage == null) {
              debugPrint('[NfcScannerService] Le tag n\'est pas au format NDEF valide.');
              NfcManager.instance.stopSession();
              completer.completeError(NfcException("Le tag n'est pas au format NDEF attendu."));
              return;
            }

            final records = cachedMessage['records'] as List;
            if (records.isEmpty) return;
            
            final record = records.first;
            final payload = record['payload'] as List<int>;
            
            // Le payload d'un record URI commence généralement par un préfixe (1 octet), 
            // mais on convertit tout en UTF-8 pour chercher notre scheme.
            // On peut aussi chercher la chaîne ASCII directement.
            final payloadString = utf8.decode(payload, allowMalformed: true);
            debugPrint('[NfcScannerService] Payload brut : $payloadString');

            // Recherche de la signature mgdrop:// (on extrait l'IP et le port)
            // ex: "...mgdrop://192.168.1.15:8080"
            if (payloadString.contains('mgdrop://')) {
              final startIndex = payloadString.indexOf('mgdrop://') + 9; // 9 = length of 'mgdrop://'
              final addressPart = payloadString.substring(startIndex).trim();
              
              final parts = addressPart.split(':');
              if (parts.length == 2) {
                final ip = parts[0];
                final port = int.tryParse(parts[1]);
                
                if (port != null) {
                  debugPrint('[NfcScannerService] NDEF lu avec succès : $ip:$port');
                  
                  // Handshake physique
                  final hasVibe = await Vibration.hasVibrator();
                  if (hasVibe == true) {
                    Vibration.vibrate(duration: 150); // Vibration courte
                  }

                  NfcManager.instance.stopSession();
                  completer.complete(ServerConnectionInfo(ipAddress: ip, port: port));
                  return;
                }
              }
            }

            debugPrint('[NfcScannerService] Le tag ne contient pas d\'informations MG Drop valides.');
            NfcManager.instance.stopSession();
            completer.completeError(NfcException("Le tag ne contient pas d'informations MG Drop valides."));
          } catch (e) {
            debugPrint('[NfcScannerService] Erreur lors de la lecture : $e');
            NfcManager.instance.stopSession();
            completer.completeError(NfcException("Erreur lors de la lecture du tag : $e"));
          }
        },
      );
    } catch (e) {
      debugPrint('[NfcScannerService] Impossible de démarrer la session NFC : $e');
      throw NfcException("Impossible de démarrer la session NFC : $e");
    }

    return completer.future;
  }

  /// Annule manuellement le scan en cours
  void cancelScan() {
    debugPrint('[NfcScannerService] Annulation du scan NFC.');
    NfcManager.instance.stopSession();
  }
}
