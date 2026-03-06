[features.html](https://github.com/user-attachments/files/25792419/features.html)
<!DOCTYPE html>
<html lang="es">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>ArcadeShellSelector — Funcionalidades</title>
  <style>
    * { margin: 0; padding: 0; box-sizing: border-box; }
    body {
      font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
      background: #0d1117;
      color: #e6edf3;
      line-height: 1.7;
      padding: 40px 20px;
    }
    .container { max-width: 900px; margin: 0 auto; }
    h1 {
      text-align: center;
      font-size: 2.2em;
      margin-bottom: 8px;
      background: linear-gradient(135deg, #58a6ff, #bc8cff);
      -webkit-background-clip: text;
      -webkit-text-fill-color: transparent;
    }
    .subtitle {
      text-align: center;
      color: #8b949e;
      font-size: 1em;
      margin-bottom: 40px;
    }
    .section {
      background: #161b22;
      border: 1px solid #30363d;
      border-radius: 12px;
      padding: 28px 32px;
      margin-bottom: 24px;
    }
    .section h2 {
      font-size: 1.5em;
      margin-bottom: 16px;
      display: flex;
      align-items: center;
      gap: 10px;
    }
    .section h2 .icon {
      font-size: 1.3em;
    }
    .launcher h2 { color: #58a6ff; }
    .configurator h2 { color: #bc8cff; }
    ul { list-style: none; padding: 0; }
    ul li {
      padding: 8px 0;
      border-bottom: 1px solid #21262d;
      display: flex;
      align-items: flex-start;
      gap: 10px;
    }
    ul li:last-child { border-bottom: none; }
    ul li .bullet {
      flex-shrink: 0;
      width: 8px;
      height: 8px;
      border-radius: 50%;
      margin-top: 8px;
    }
    .launcher .bullet { background: #58a6ff; }
    .configurator .bullet { background: #bc8cff; }
    strong { color: #f0f6fc; }
    .tag {
      display: inline-block;
      font-size: 0.7em;
      padding: 2px 8px;
      border-radius: 10px;
      font-weight: 600;
      vertical-align: middle;
      margin-left: 6px;
    }
    .tag-video { background: #1f3a2a; color: #3fb950; }
    .tag-audio { background: #3b2a1a; color: #d29922; }
    .tag-input { background: #2a1f3a; color: #bc8cff; }
    .tag-ui { background: #1a2a3b; color: #58a6ff; }
    .footer {
      text-align: center;
      color: #484f58;
      font-size: 0.85em;
      margin-top: 32px;
    }
  </style>
</head>
<body>
  <div class="container">
    <h1>ArcadeShellSelector</h1>
    <p class="subtitle">Selector de shells arcade para Windows &middot; .NET 10 &middot; WinForms</p>

    <!-- LAUNCHER -->
    <div class="section launcher">
      <h2><span class="icon">🕹️</span> Launcher (App principal)</h2>
      <ul>
        <li>
          <span class="bullet"></span>
          <span><strong>Selector visual de shells/frontends arcade</strong> — Muestra las opciones configuradas como imágenes grandes con efecto zoom al pasar el ratón o con gamepad. <span class="tag tag-ui">UI</span></span>
        </li>
        <li>
          <span class="bullet"></span>
          <span><strong>Fondo de vídeo</strong> — Reproduce un vídeo en bucle (MP4, MKV, AVI…) detrás de la interfaz usando LibVLC. <span class="tag tag-video">Vídeo</span></span>
        </li>
        <li>
          <span class="bullet"></span>
          <span><strong>Música de fondo</strong> — Reproduce módulos (MOD, XM) desde una carpeta configurable, con volumen y dispositivo de audio ajustables. <span class="tag tag-audio">Audio</span></span>
        </li>
        <li>
          <span class="bullet"></span>
          <span><strong>Analizador de espectro</strong> — Visualización de barras de audio en tiempo real (WASAPI loopback), con ventana click-through para no bloquear la interfaz. <span class="tag tag-audio">Audio</span></span>
        </li>
        <li>
          <span class="bullet"></span>
          <span><strong>Soporte XInput</strong> — Navegación y selección con mando/gamepad (D-Pad + botón A). <span class="tag tag-input">Input</span></span>
        </li>
        <li>
          <span class="bullet"></span>
          <span><strong>Lanzamiento de aplicaciones</strong> — Ejecuta el .exe seleccionado y espera a que el proceso termine. Soporta rutas UNC de red con espera configurable.</span>
        </li>
        <li>
          <span class="bullet"></span>
          <span><strong>Modo shell</strong> — Pensado para ejecutarse como shell de Windows; lanza <code>explorer.exe</code> al salir con ESC.</span>
        </li>
        <li>
          <span class="bullet"></span>
          <span><strong>Always on top</strong> — Opción configurable para mantener la ventana siempre visible. <span class="tag tag-ui">UI</span></span>
        </li>
        <li>
          <span class="bullet"></span>
          <span><strong>Logging / depuración</strong> — Registro opcional de eventos a fichero para diagnóstico.</span>
        </li>
      </ul>
    </div>

    <!-- CONFIGURATOR -->
    <div class="section configurator">
      <h2><span class="icon">⚙️</span> Configurator (Editor de configuración)</h2>
      <ul>
        <li>
          <span class="bullet"></span>
          <span><strong>Editor visual de config.json</strong> — Interfaz con pestañas para editar todos los ajustes sin tocar JSON a mano.</span>
        </li>
        <li>
          <span class="bullet"></span>
          <span><strong>Pestaña General</strong> — Título de la app, always on top, activar/desactivar logging.</span>
        </li>
        <li>
          <span class="bullet"></span>
          <span><strong>Pestaña Rutas</strong> — Carpeta raíz de herramientas, carpeta de imágenes, vídeo de fondo (con botón "…" para explorar).</span>
        </li>
        <li>
          <span class="bullet"></span>
          <span><strong>Pestaña Música</strong> — Activar/desactivar música, carpeta, volumen, dispositivo de audio.</span>
        </li>
        <li>
          <span class="bullet"></span>
          <span><strong>Pestaña App Options</strong> — Tabla editable con columnas para etiqueta, ruta del exe, miniatura de imagen, proceso de espera. Cada fila tiene botones "…" para explorar exe e imagen.</span>
        </li>
        <li>
          <span class="bullet"></span>
          <span><strong>Miniaturas en la tabla</strong> — La columna de imagen muestra un preview de 48×48 en vez de texto plano. <span class="tag tag-ui">UI</span></span>
        </li>
        <li>
          <span class="bullet"></span>
          <span><strong>Detección de cambios</strong> — El botón "Guardar" solo se activa cuando hay cambios sin guardar.</span>
        </li>
        <li>
          <span class="bullet"></span>
          <span><strong>Guardado multi-destino</strong> — Escribe config.json en todas las ubicaciones encontradas (fuente + carpetas de salida) para que los cambios se apliquen sin recompilar.</span>
        </li>
        <li>
          <span class="bullet"></span>
          <span><strong>Botón Launch App</strong> — Lanza el Launcher directamente (verificando que no esté ya en ejecución) y cierra el Configurator.</span>
        </li>
        <li>
          <span class="bullet"></span>
          <span><strong>Icono de la app</strong> — Usa el mismo icono (app.ico) tanto en el exe como en la barra de título.</span>
        </li>
      </ul>
    </div>

    <p class="footer">2026 &middot; Trume76 &middot; ArcadeShellSelector</p>
  </div>
</body>
</html>
