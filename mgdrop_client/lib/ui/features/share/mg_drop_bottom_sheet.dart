import 'package:flutter/material.dart';
import 'dynamic_port_overlay.dart';

/// Les états possibles du bottom sheet de transfert MG Drop
enum TransferState {
  waitingForNfc,
  transferring,
  success,
}

/// Le widget Bottom Sheet élégant qui s'affiche au-dessus des autres applications.
class MGDropBottomSheet extends StatefulWidget {
  final TransferState initialState;
  final ValueNotifier<TransferState>? stateNotifier;
  final ValueNotifier<double>? progressNotifier; // Optionnel : de 0.0 à 1.0
  final VoidCallback onCancel;

  const MGDropBottomSheet({
    super.key,
    this.initialState = TransferState.waitingForNfc,
    this.stateNotifier,
    this.progressNotifier,
    required this.onCancel,
  });

  @override
  State<MGDropBottomSheet> createState() => _MGDropBottomSheetState();
}

class _MGDropBottomSheetState extends State<MGDropBottomSheet> {
  late TransferState _currentState;

  @override
  void initState() {
    super.initState();
    _currentState = widget.initialState;
    widget.stateNotifier?.addListener(_onStateChanged);
  }

  @override
  void dispose() {
    widget.stateNotifier?.removeListener(_onStateChanged);
    super.dispose();
  }

  void _onStateChanged() {
    if (widget.stateNotifier != null) {
      setState(() {
        _currentState = widget.stateNotifier!.value;
      });
      
      // Si on passe en succès, on lance l'animation "Dynamic Port"
      if (_currentState == TransferState.success) {
        DynamicPortOverlay.showSuccess(context);
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    // Design System MG Drop
    const Color bgColor = Color(0xFFF5FEFF); // Blanc glacé
    const Color primaryColor = Color(0xFF0E2F76); // Bleu marine profond
    const Color secondaryColor = Color(0xFFAAC0E1); // Bleu pastel
    const Color successColor = Color(0xFF34C78A); // Vert succès

    return Container(
      width: double.infinity,
      decoration: BoxDecoration(
        color: bgColor,
        borderRadius: const BorderRadius.vertical(top: Radius.circular(30)),
        boxShadow: [
          BoxShadow(
            color: primaryColor.withOpacity(0.12),
            blurRadius: 32,
            offset: const Offset(0, -12),
          ),
        ],
      ),
      child: SafeArea(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(24, 12, 24, 48),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              // Drag Handle
              Container(
                width: 48,
                height: 6,
                decoration: BoxDecoration(
                  color: primaryColor.withOpacity(0.2),
                  borderRadius: BorderRadius.circular(3),
                ),
                margin: const EdgeInsets.only(bottom: 40),
              ),

              // Animation Container selon l'état
              SizedBox(
                height: 192,
                width: 192,
                child: Center(
                  child: AnimatedSwitcher(
                    duration: const Duration(milliseconds: 500),
                    child: _buildAnimationForState(_currentState, primaryColor, secondaryColor, successColor),
                  ),
                ),
              ),

              const SizedBox(height: 32),

              // Typographie selon l'état
              AnimatedSwitcher(
                duration: const Duration(milliseconds: 300),
                child: _buildTextForState(_currentState, primaryColor),
              ),

              const SizedBox(height: 32),

              // Bouton Annuler
              if (_currentState != TransferState.success)
                TextButton(
                  onPressed: widget.onCancel,
                  style: TextButton.styleFrom(
                    backgroundColor: secondaryColor.withOpacity(0.2),
                    foregroundColor: primaryColor,
                    padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 12),
                    shape: RoundedRectangleBorder(
                      borderRadius: BorderRadius.circular(9999),
                    ),
                  ),
                  child: const Text(
                    'Annuler',
                    style: TextStyle(
                      fontFamily: 'SF Pro',
                      fontFamilyFallback: ['Inter'],
                      fontSize: 14,
                      fontWeight: FontWeight.w500,
                    ),
                  ),
                ),
            ],
          ),
        ),
      ),
    );
  }

  Widget _buildAnimationForState(TransferState state, Color primary, Color secondary, Color success) {
    switch (state) {
      case TransferState.waitingForNfc:
        return _WaitingNfcAnimation(key: const ValueKey('waiting'), primaryColor: primary, secondaryColor: secondary);
      case TransferState.transferring:
        return _TransferProgressAnimation(
          key: const ValueKey('transferring'),
          primaryColor: primary,
          secondaryColor: secondary,
          progressNotifier: widget.progressNotifier,
        );
      case TransferState.success:
        return Icon(
          Icons.check_circle,
          key: const ValueKey('success'),
          color: success,
          size: 80,
        );
    }
  }

  Widget _buildTextForState(TransferState state, Color primaryColor) {
    String title = '';
    String subtitle = '';

    switch (state) {
      case TransferState.waitingForNfc:
        title = 'En attente du PC...';
        subtitle = 'Approchez le téléphone du capteur';
        break;
      case TransferState.transferring:
        title = 'Envoi en cours...';
        subtitle = 'Veuillez patienter';
        break;
      case TransferState.success:
        title = 'Transfert terminé !';
        subtitle = 'Le fichier est arrivé sur le PC';
        break;
    }

    return Column(
      key: ValueKey(state),
      children: [
        Text(
          title,
          textAlign: TextAlign.center,
          style: TextStyle(
            fontFamily: 'SF Pro',
            fontFamilyFallback: const ['Inter'],
            fontSize: 24,
            fontWeight: FontWeight.w600,
            color: primaryColor,
          ),
        ),
        const SizedBox(height: 8),
        Text(
          subtitle,
          textAlign: TextAlign.center,
          style: TextStyle(
            fontFamily: 'SF Pro',
            fontFamilyFallback: const ['Inter'],
            fontSize: 16,
            fontWeight: FontWeight.w400,
            color: primaryColor.withOpacity(0.6),
          ),
        ),
      ],
    );
  }
}

// ── SOUS-WIDGET 1 : Animation d'ondes (Waiting NFC) ─────────────────────

class _WaitingNfcAnimation extends StatefulWidget {
  final Color primaryColor;
  final Color secondaryColor;

  const _WaitingNfcAnimation({super.key, required this.primaryColor, required this.secondaryColor});

  @override
  State<_WaitingNfcAnimation> createState() => _WaitingNfcAnimationState();
}

class _WaitingNfcAnimationState extends State<_WaitingNfcAnimation> with SingleTickerProviderStateMixin {
  late AnimationController _controller;

  @override
  void initState() {
    super.initState();
    _controller = AnimationController(
      vsync: this,
      duration: const Duration(seconds: 2),
    )..repeat();
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Stack(
      alignment: Alignment.center,
      children: [
        // Ondes douces (CustomPainter)
        AnimatedBuilder(
          animation: _controller,
          builder: (context, child) {
            return CustomPaint(
              size: const Size(192, 192),
              painter: _RipplePainter(_controller.value, widget.secondaryColor),
            );
          },
        ),
        // Icône centrale
        Container(
          width: 80,
          height: 80,
          decoration: BoxDecoration(
            color: widget.primaryColor,
            shape: BoxShape.circle,
            boxShadow: [
              BoxShadow(
                color: widget.primaryColor.withOpacity(0.4),
                blurRadius: 16,
                offset: const Offset(0, 4),
              ),
            ],
            border: Border.all(color: Colors.white.withOpacity(0.2), width: 2),
          ),
          child: const Center(
            child: Icon(
              Icons.contactless,
              color: Colors.white,
              size: 40,
            ),
          ),
        ),
      ],
    );
  }
}

class _RipplePainter extends CustomPainter {
  final double animationValue;
  final Color color;

  _RipplePainter(this.animationValue, this.color);

  @override
  void paint(Canvas canvas, Size size) {
    final Paint paint = Paint()..style = PaintingStyle.fill;
    final center = Offset(size.width / 2, size.height / 2);
    final maxRadius = size.width / 2;

    for (int i = 0; i < 2; i++) {
      // Décalage pour avoir plusieurs ondes
      double value = (animationValue + (i * 0.5)) % 1.0;
      paint.color = color.withOpacity((1.0 - value) * 0.5); // Fade out en s'agrandissant
      canvas.drawCircle(center, maxRadius * value, paint);
    }
  }

  @override
  bool shouldRepaint(_RipplePainter oldDelegate) {
    return oldDelegate.animationValue != animationValue;
  }
}

// ── SOUS-WIDGET 2 : Jauge de progression (Transfert TCP) ─────────────────

class _TransferProgressAnimation extends StatelessWidget {
  final Color primaryColor;
  final Color secondaryColor;
  final ValueNotifier<double>? progressNotifier;

  const _TransferProgressAnimation({
    super.key,
    required this.primaryColor,
    required this.secondaryColor,
    this.progressNotifier,
  });

  @override
  Widget build(BuildContext context) {
    return Stack(
      alignment: Alignment.center,
      children: [
        // Jauge circulaire
        SizedBox(
          width: 120,
          height: 120,
          child: progressNotifier == null
              ? CircularProgressIndicator(
                  valueColor: AlwaysStoppedAnimation<Color>(secondaryColor),
                  backgroundColor: secondaryColor.withOpacity(0.1),
                  strokeWidth: 8,
                )
              : ValueListenableBuilder<double>(
                  valueListenable: progressNotifier!,
                  builder: (context, value, child) {
                    return CircularProgressIndicator(
                      value: value,
                      valueColor: AlwaysStoppedAnimation<Color>(secondaryColor),
                      backgroundColor: secondaryColor.withOpacity(0.1),
                      strokeWidth: 8,
                      strokeCap: StrokeCap.round,
                    );
                  },
                ),
        ),
        // Pourcentage au centre
        if (progressNotifier != null)
          ValueListenableBuilder<double>(
            valueListenable: progressNotifier!,
            builder: (context, value, child) {
              return Text(
                '${(value * 100).toInt()}%',
                style: TextStyle(
                  fontFamily: 'SF Pro',
                  fontFamilyFallback: const ['Inter'],
                  fontSize: 24,
                  fontWeight: FontWeight.bold,
                  color: primaryColor,
                ),
              );
            },
          )
        else
          Icon(
            Icons.cloud_upload_outlined,
            color: primaryColor,
            size: 40,
          ),
      ],
    );
  }
}
