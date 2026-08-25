import 'dart:async';
import 'package:flutter/material.dart';

/// Service Singleton gérant l'affichage de la notification "Dynamic Port" (île dynamique).
/// 
/// S'affiche par-dessus l'écran via le système d'Overlay de Flutter.
class DynamicPortOverlay {
  static OverlayEntry? _currentOverlay;

  /// Affiche l'animation de succès "Dynamic Port" depuis le haut de l'écran.
  /// 
  /// Ferme automatiquement l'animation précédente si elle est encore visible.
  static void showSuccess(BuildContext context) {
    // Si un overlay est déjà affiché, on le retire immédiatement pour éviter les superpositions.
    if (_currentOverlay != null) {
      _currentOverlay?.remove();
      _currentOverlay = null;
    }

    final overlayState = Overlay.of(context);
    
    _currentOverlay = OverlayEntry(
      builder: (context) {
        return _DynamicPortWidget(
          onDismissed: () {
            // Nettoyage une fois l'animation terminée
            if (_currentOverlay != null) {
              _currentOverlay?.remove();
              _currentOverlay = null;
            }
          },
        );
      },
    );

    overlayState.insert(_currentOverlay!);
  }
}

/// Le Widget avec état qui gère sa propre animation de cycle de vie (Entrée, Maintien, Sortie).
class _DynamicPortWidget extends StatefulWidget {
  final VoidCallback onDismissed;

  const _DynamicPortWidget({required this.onDismissed});

  @override
  State<_DynamicPortWidget> createState() => _DynamicPortWidgetState();
}

class _DynamicPortWidgetState extends State<_DynamicPortWidget> with SingleTickerProviderStateMixin {
  late AnimationController _controller;
  late Animation<double> _slideAnimation;
  late Animation<double> _opacityAnimation;

  @override
  void initState() {
    super.initState();
    
    // Durée de l'animation d'apparition
    _controller = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 600),
    );

    // Animation de translation verticale (Slide Down)
    // De -100 (hors écran) à 0 (position finale)
    _slideAnimation = Tween<double>(begin: -100.0, end: 0.0).animate(
      CurvedAnimation(
        parent: _controller,
        curve: Curves.elasticOut, // Effet organique fluide typique d'une Dynamic Island
      ),
    );

    // Animation d'opacité (Fade In)
    _opacityAnimation = Tween<double>(begin: 0.0, end: 1.0).animate(
      CurvedAnimation(
        parent: _controller,
        curve: const Interval(0.0, 0.5, curve: Curves.easeIn),
      ),
    );

    _playSequence();
  }

  Future<void> _playSequence() async {
    // 1. Apparition
    await _controller.forward();
    
    // 2. Maintien pendant 2 secondes
    await Future.delayed(const Duration(seconds: 2));
    
    // Vérifier si le widget est toujours monté avant de lancer la disparition
    if (mounted) {
      // 3. Disparition (Reverse)
      await _controller.reverse();
      
      // 4. Destruction de l'Overlay
      widget.onDismissed();
    }
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    // Hauteur de la barre d'état (encoche/caméra frontale)
    final topPadding = MediaQuery.of(context).padding.top;
    
    // Design System (Inspiré de Stitch P1_S3_Succes.html)
    const Color bgColor = Color(0xFF000000); // Noir absolu pour fusionner avec la caméra (ou 0xFF0E2F76)
    const Color textColor = Color(0xFFF5FEFF); // Blanc glacé
    const Color iconColor = Color(0xFF00DFC2); // Vert Cyan (Tertiary fixed dim)

    return Positioned(
      top: topPadding + 8, // Juste en dessous de l'encoche
      left: 0,
      right: 0,
      child: AnimatedBuilder(
        animation: _controller,
        builder: (context, child) {
          return Transform.translate(
            offset: Offset(0, _slideAnimation.value),
            child: Opacity(
              opacity: _opacityAnimation.value,
              child: Align(
                alignment: Alignment.topCenter,
                child: Material(
                  color: Colors.transparent, // Material requis pour le rendu de texte dans un Overlay
                  child: Container(
                    padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 12),
                    decoration: BoxDecoration(
                      color: bgColor,
                      borderRadius: BorderRadius.circular(50), // Très arrondi (Pilule)
                      boxShadow: [
                        BoxShadow(
                          color: const Color(0xFF0E2F76).withOpacity(0.2), // Ombre bleutée
                          blurRadius: 32,
                          offset: const Offset(0, 12),
                        )
                      ],
                      border: Border.all(color: Colors.white.withOpacity(0.1), width: 1),
                    ),
                    child: Row(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        const Icon(
                          Icons.check_circle,
                          color: iconColor,
                          size: 20,
                        ),
                        const SizedBox(width: 12),
                        const Text(
                          'Envoyé',
                          style: TextStyle(
                            fontFamily: 'SF Pro',
                            fontFamilyFallback: ['Inter'],
                            fontSize: 14,
                            fontWeight: FontWeight.w600,
                            color: textColor,
                          ),
                        ),
                      ],
                    ),
                  ),
                ),
              ),
            ),
          );
        },
      ),
    );
  }
}
