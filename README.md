ArcadeShellSelector — Resumen de funcionalidades

Launcher (App principal)
  Selector visual de shells/frontends arcade: muestra las opciones configuradas como imágenes grandes con efecto zoom al pasar el ratón o con el gamepad.
  Fondo de vídeo: reproduce un vídeo en bucle (MP4, MKV, etc.) detrás de la interfaz usando LibVLC.
  Música de fondo: reproduce módulos (MOD, XM) desde una carpeta configurable, con volumen y dispositivo de audio ajustables.
  Analizador de espectro: visualización de barras de audio en tiempo real (WASAPI loopback), con ventana click-through para no bloquear la interfaz.
  Soporte XInput: navegación y selección con mando/gamepad (D-Pad + botón A).
  Lanzamiento de aplicaciones: ejecuta el .exe seleccionado y espera a que el proceso termine (soporta rutas UNC de red con espera configurable).
  Modo shell: pensado para ejecutarse como shell de Windows (lanza explorer.exe al salir con ESC).
  Always on top: opción configurable para mantener la ventana siempre visible.
  Logging/depuración: registro opcional de eventos a fichero para diagnóstico.

Configurator (Editor de configuración)
  Editor visual de config.json: interfaz con pestañas para editar todos los ajustes sin tocar JSON a mano.
  General: título de la app, always on top, activar/desactivar logging.
  Rutas: carpeta raíz de herramientas, carpeta de imágenes, vídeo de fondo (con botón "..." para explorar).
  Música: activar/desactivar música, carpeta, volumen, dispositivo de audio.
  Opciones (Apps): tabla editable con columnas para etiqueta, ruta del exe, miniatura de imagen, proceso de espera — cada fila tiene botones "..." para explorar exe e imagen.
  Miniaturas en la tabla: la columna de imagen muestra un preview de 48×48 en vez de texto plano.
  Detección de cambios: el botón "Guardar" solo se activa cuando hay cambios sin guardar.
  Guardado multi-destino: escribe config.json en todas las ubicaciones encontradas (fuente + carpetas de salida del build) para que los cambios se apliquen sin recompilar.
