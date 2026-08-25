import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';
import 'package:flutter/foundation.dart';
import 'package:path/path.dart' as p;

/// Exception personnalisée pour les erreurs réseau durant le transfert.
class NetworkException implements Exception {
  final String message;
  NetworkException(this.message);

  @override
  String toString() => 'NetworkException: $message';
}

/// Service dédié au transfert TCP (Big Endian) vers le serveur Windows.
/// 
/// Rôle :
/// - Initier une connexion Socket vers l'IP/Port du serveur.
/// - Envoyer les métadonnées (taille du nom, nom du fichier, taille du fichier).
/// - Streamer le contenu du fichier (octets) sans le charger en RAM.
/// - Gérer les erreurs (connexion coupée) proprement.
class TcpTransferService {
  
  /// Transfère un fichier vers le PC hôte identifié par [ipAddress] et [port].
  /// 
  /// Utilise un [Stream] pour la lecture du fichier, ce qui empêche les OOM
  /// (Out Of Memory) sur les gros fichiers.
  Future<void> sendFile({
    required File file,
    required String ipAddress,
    required int port,
    Function(double progress)? onProgress,
  }) async {
    Socket? socket;
    
    try {
      if (!await file.exists()) {
        throw NetworkException("Le fichier sélectionné n'existe pas ou est inaccessible.");
      }

      final fileName = p.basename(file.path);
      final fileSize = await file.length();
      
      debugPrint('[TcpTransferService] Tentative de connexion à $ipAddress:$port ...');
      
      // 1. Initialiser la connexion TCP
      socket = await Socket.connect(ipAddress, port, timeout: const Duration(seconds: 10));
      debugPrint('[TcpTransferService] Connexion Socket ouverte.');

      // 2. Préparer les métadonnées (Protocole Binaire Big Endian)
      // Format : [NameLength (4 bytes)] + [Name (UTF-8)] + [FileSize (8 bytes)]
      final nameBytes = utf8.encode(fileName);
      final headerBuilder = BytesBuilder();

      // Taille du nom (4 octets, Big Endian)
      final nameLenData = ByteData(4);
      nameLenData.setInt32(0, nameBytes.length, Endian.big);
      headerBuilder.add(nameLenData.buffer.asUint8List());

      // Nom du fichier en UTF-8
      headerBuilder.add(nameBytes);

      // Taille du fichier (8 octets, Big Endian)
      final sizeData = ByteData(8);
      sizeData.setInt64(0, fileSize, Endian.big);
      headerBuilder.add(sizeData.buffer.asUint8List());

      // Envoyer le Header
      debugPrint('[TcpTransferService] Envoi des métadonnées (fichier: $fileName, taille: $fileSize octets)...');
      socket.add(headerBuilder.toBytes());

      // 3. Streamer le fichier par blocs pour ne pas saturer la RAM (file.openRead())
      debugPrint('[TcpTransferService] Début du streaming des octets...');
      
      int totalSent = 0;
      await for (final chunk in file.openRead()) {
        socket.add(chunk);
        totalSent += chunk.length;
        if (onProgress != null && fileSize > 0) {
          onProgress(totalSent / fileSize);
        }
      }
      
      // On s'assure que tout est bien parti
      await socket.flush();
      
      debugPrint('[TcpTransferService] Flux d\'octets terminé avec succès.');

    } on SocketException catch (e) {
      debugPrint('[TcpTransferService] Erreur Socket : ${e.message}');
      throw NetworkException("Erreur de connexion réseau au PC : ${e.message}");
    } catch (e) {
      debugPrint('[TcpTransferService] Erreur inattendue : $e');
      throw NetworkException("Erreur lors du transfert : $e");
    } finally {
      // 4. Fermeture propre du socket
      if (socket != null) {
        try {
          await socket.close();
          debugPrint('[TcpTransferService] Socket fermé proprement.');
        } catch (e) {
          debugPrint('[TcpTransferService] Erreur lors de la fermeture du socket : $e');
        }
      }
    }
  }
}
