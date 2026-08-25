import 'dart:async';
import 'dart:io';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:receive_sharing_intent/receive_sharing_intent.dart';
import 'ui/features/share/mg_drop_bottom_sheet.dart';
import 'data/services/nfc_scanner_service.dart';
import 'data/services/tcp_transfer_service.dart';
void main() {
  WidgetsFlutterBinding.ensureInitialized();
  // Rend la barre de statut transparente
  SystemChrome.setSystemUIOverlayStyle(
    const SystemUiOverlayStyle(
      statusBarColor: Colors.transparent,
      systemNavigationBarColor: Colors.transparent,
    ),
  );
  runApp(const MGDropApp());
}

class MGDropApp extends StatelessWidget {
  const MGDropApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      debugShowCheckedModeBanner: false,
      title: 'MG Drop',
      theme: ThemeData(
        useMaterial3: true,
        // Fond transparent pour laisser voir l'app (Galerie) derrière
        scaffoldBackgroundColor: Colors.transparent, dialogTheme: DialogThemeData(backgroundColor: Colors.transparent),
      ),
      // On utilise un constructeur avec fond transparent
      home: const ShareReceiverScreen(),
    );
  }
}

class ShareReceiverScreen extends StatefulWidget {
  const ShareReceiverScreen({super.key});

  @override
  State<ShareReceiverScreen> createState() => _ShareReceiverScreenState();
}

class _ShareReceiverScreenState extends State<ShareReceiverScreen> {
  late StreamSubscription _intentDataStreamSubscription;
  List<SharedMediaFile>? _sharedFiles;
  
  final ValueNotifier<TransferState> _transferState = ValueNotifier(TransferState.waitingForNfc);
  final ValueNotifier<double> _transferProgress = ValueNotifier(0.0);

  final NfcScannerService _nfcService = NfcScannerService();
  final TcpTransferService _tcpService = TcpTransferService();

  @override
  void initState() {
    super.initState();
    _initSharingIntent();
  }

  void _initSharingIntent() {
    // Écoute des partages lorsque l'application est déjà en mémoire
    _intentDataStreamSubscription = ReceiveSharingIntent.instance.getMediaStream().listen((List<SharedMediaFile> value) {
      if (mounted) {
        setState(() {
          _sharedFiles = value;
        });
      }
      _handleSharedFiles(value);
    }, onError: (err) {
      debugPrint("getMediaStream error: $err");
    });

    // Récupération des partages si l'application était fermée
    ReceiveSharingIntent.instance.getInitialMedia().then((List<SharedMediaFile> value) {
      if (value.isNotEmpty) {
        if (mounted) {
          setState(() {
            _sharedFiles = value;
          });
        }
        _handleSharedFiles(value);
      }
    });
  }

  Future<void> _handleSharedFiles(List<SharedMediaFile> files) async {
    if (files.isEmpty) return;
    
    debugPrint("Fichier partagé reçu : ${files.first.path}");
    
    // Étape 1 : Attente du scan NFC
    _transferState.value = TransferState.waitingForNfc;
    _transferProgress.value = 0.0;

    final nfcAvailable = await _nfcService.isNfcAvailable();
    if (!nfcAvailable) {
      debugPrint("Le capteur NFC n'est pas disponible ou est désactivé.");
      // Gérer l'erreur (ex: afficher un message et fermer)
      return;
    }

    try {
      final credentials = await _nfcService.scanForPcConnection();
      debugPrint("Scan NFC réussi ! PC détecté sur ${credentials.ipAddress}:${credentials.port}");

      if (!mounted) return;

      // Étape 2 : Handshake Matériel
      // Le retour haptique est déjà géré dans NfcScannerService (vibrate)
      _transferState.value = TransferState.transferring;

      // Étape 3 : Transfert Réseau
      final file = File(files.first.path);
      await _tcpService.sendFile(
        file: file,
        ipAddress: credentials.ipAddress,
        port: credentials.port,
        onProgress: (progress) {
          if (mounted) {
            _transferProgress.value = progress;
          }
        },
      );

      if (!mounted) return;

      // Étape 4 : Succès
      _transferState.value = TransferState.success;
      
      // On masque l'interface après l'apparition du Dynamic Port (géré dans MGDropBottomSheet)
      Future.delayed(const Duration(seconds: 3), () {
        if (mounted) {
          SystemNavigator.pop();
        }
      });
      
    } catch (e) {
      debugPrint("Erreur de transfert : $e");
      // Gérer l'erreur (fermer ou réinitialiser)
      if (mounted) {
        SystemNavigator.pop();
      }
    }
  }

  @override
  void dispose() {
    _intentDataStreamSubscription.cancel();
    _transferState.dispose();
    _transferProgress.dispose();
    // Arrêter le NFC proprement si annulé
    _nfcService.cancelScan();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    // Si aucun fichier n'est partagé, on ne dessine rien (transparent)
    if (_sharedFiles == null || _sharedFiles!.isEmpty) {
      return const Scaffold(
        backgroundColor: Colors.transparent,
      );
    }

    return Scaffold(
      backgroundColor: Colors.black45, // Voile assombrissant léger
      body: Align(
        alignment: Alignment.bottomCenter,
        // On intègre le Bottom Sheet créé précédemment
        child: MGDropBottomSheet(
          initialState: TransferState.waitingForNfc,
          stateNotifier: _transferState,
          progressNotifier: _transferProgress,
          onCancel: () {
            // Quitte le processus de partage et ferme l'app
            SystemNavigator.pop();
          },
        ),
      ),
    );
  }
}
