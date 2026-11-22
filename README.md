# Checkpoint

App de Windows para crear y editar tus listas de juegos (`.txt` y `.md`) con el mismo formato que
`Juegos que me he pasado.txt`, sin tener que acordarte de la sintaxis.

**Para abrirla:** acceso directo «Checkpoint» del Escritorio o del menú Inicio, o
`publish\Checkpoint.exe`. Es un solo archivo y no necesita instalar nada: usa WebView2, que ya viene
con Windows 11. También puedes arrastrar un `.txt` o un `.md` al `.exe` o a la ventana.

## Qué hace

- **Secciones en pestañas:** Completados, Inconclusos y Por jugar, además de Jugando ahora,
  Completados al 100%, Abandonados, Lista de deseos o las que crees tú.
- **Sagas y géneros:** `Resident Evil:` o `(Indies)`. Al escribir un título la app propone su saga a
  partir de un diccionario de unas 150 sagas y de las que ya tienes.
- **Ficha de cada juego:** título, plataformas (con sugerencias), fecha, nota, puntuación (1–10), horas,
  % de logros, tienda enlazada y campos libres `{…}`.
- **Marcar como completado** (`Ctrl+Intro`): lo mueve a Completados, a su misma saga, con la fecha de hoy.
- **Vista de portadas o lista compacta**, búsqueda en todas las secciones y filtro por plataforma.
- **Duración estimada** de cada juego (historia principal y, opcional, al 100 %) en la ficha. En «Por jugar», la
  barra de estado suma las horas estimadas de los pendientes que la tienen.
- **«¿Qué juego ahora?»** (`Ctrl+J` o el botón de «Por jugar»): propone pendientes según el tiempo que tengas
  (corto < 8 h, medio 8–20 h, largo > 20 h), las sagas con entregas en Completados o Inconclusos y la plataforma.
  Primero salen las continuaciones de sagas y después los más cortos. Desde cada sugerencia puedes ir al juego o,
  si existe «Jugando ahora», empezarlo.
- **Estadísticas** (`Ctrl+E` o el botón de la barra superior): completados por año, horas jugadas, nota media,
  plataformas y sagas con más juegos completados, calculadas al momento con la lista abierta. Los gráficos son SVG
  y cada uno tiene sus datos en una tabla.
- **Resumen del año en PNG:** elige un año y la app dibuja en este PC una tarjeta tipo póster con los juegos
  completados ese año, sus portadas ya descargadas (o una portada tipográfica si falta), juegos, horas, nota media
  y mejores puntuaciones. Se guarda con el diálogo de Windows, sin conexión.
- **Cambios hechos fuera de la app** (OneDrive, Syncthing, otro editor): si no tienes cambios sin guardar, la lista
  se recarga sola y conserva la pestaña, la búsqueda, el filtro y la posición. Si los tienes, un aviso te deja
  elegir entre «Recargar desde disco» o «Mantener mis cambios» (al guardar, la versión de fuera queda en el `.bak`).
  `Ctrl+Z` deshace una recarga.
- **Bienvenida la primera vez:** cuatro pasos para el idioma, el tema y la vista, las opciones al guardar, las
  cuentas (opcional) y la lista con la que empezar. Se puede volver a abrir desde Ajustes.
- **Continuar con la última lista:** al abrir la app sin archivo pregunta si quieres seguir con la última lista. Con
  «No volver a preguntar», tu respuesta queda para siempre: la abre directamente o empieza en la pantalla de inicio.
  Se cambia en Ajustes › Al abrir la app.
- **Pantalla completa sin bordes** (`F11` o el botón de la barra superior): sin barra de título ni marco y por encima
  de la barra de tareas. `F11` o `Esc` vuelven a la ventana, que conserva su tamaño y posición.
- **Deshacer y rehacer** (`Ctrl+Z` / `Ctrl+Y`) y aviso de cambios sin guardar al cerrar.
- **Copia `.bak`** la primera vez que guardas un archivo en cada sesión.
- **Vista «Texto»:** muestra exactamente lo que se va a guardar, en `.txt` o `.md`, y deja editarlo a mano.
- **Importar** desde Steam, Xbox, Epic, GOG, EA y Ubisoft (ver abajo). Antes de añadir nada te enseña la
  lista con la sección y la saga propuestas para cada juego y te marca los que ya tienes.

## Formato `.txt`

```
Lista de los juegos que me he pasado (Actualizado el 21/11/2026)   ← la fecha se pone al día al guardar

Resident Evil:                                ← saga
                                              
-Resident Evil 4 (Remake) - PC #10/07/2026#   ← juego - plataforma #fecha#
-Super Mario Bros [emulación] - GBA/SNES/NES  ← [nota] antes o después de la plataforma
-Hades - PC {★ 9/10} {42 h} {Logros 100%} {steam:1145360} {Dificultad: Difícil}   ← campos nuevos

(Indies)                                      ← género

[Juegos Inconclusos que quisiera terminar]    ← nota separadora

<Inconclusos>                                 ← sección (lo de antes de la primera son los Completados)
```

Los campos `{…}` son opcionales: `{★ 9/10}` es la puntuación, `{42 h}` las horas, `{Logros 100%}` los
logros, `{steam:id}` / `{xbox:id}` / `{ra:id}` la tienda enlazada (`ra` es RetroAchievements), `{Hardcore: 85%}` los logros en modo Hardcore, `{ttb: 12h}` o `{ttb: 12h / 35h}` la duración estimada (historia
principal / completista; en el `.md` aparece como `⌛ ttb: 12h / 35h` y vuelve igual al `.txt`), `{sgdb:id}` la portada de SteamGridDB, y `{clave: valor}` o `{etiqueta}` cualquier otro dato.

**Tu archivo se respeta:** las líneas que no tocas se guardan exactamente igual, con los mismos espacios,
la misma codificación (UTF-8, ANSI…) y los mismos saltos de línea. Abrir y guardar sin cambios deja el
archivo idéntico byte a byte; está comprobado con tu lista.

## Formato `.md` (Markdown)

«Guardar como» › Markdown convierte la lista, y desde `.md` se puede volver a `.txt` sin perder nada.
Aprovecha lo que ofrece Markdown, y cada opción se activa o desactiva en Ajustes:

- cabecera YAML (`---`) con el título, la fecha y las opciones;
- `#` para el título, `##` para las secciones (con emoji y número de juegos) y `###` para las sagas;
- tabla resumen con los juegos de cada sección;
- listas de tareas: `- [x]` en los completados y `- [ ]` en el resto, y ~~tachado~~ en Abandonados;
- **negrita** para el título, *cursiva* para la nota y `código` para la plataforma;
- enlaces a la tienda (Steam o Microsoft Store) y miniaturas de portada;
- sagas plegables con `<details>` (en GitHub y Obsidian);
- líneas `---` entre secciones.

## Conexión con plataformas

| Plataforma | Qué se puede traer | Qué hace falta |
|---|---|---|
| Steam (tu cuenta) | Toda la biblioteca, horas, % de logros, jugados hace poco | Clave de la [Steam Web API](https://steamcommunity.com/dev/apikey) y tu SteamID o enlace del perfil, con los «Detalles del juego» en público |
| Steam (este PC) | Juegos instalados y horas | Nada (Steam no está instalado ahora en este PC) |
| Xbox (este PC) | Juegos de la app Xbox / Game Pass (`X:\XboxGames`) | Nada. Encuentra Balatro y SUPERHOT: MIND CONTROL DELETE |
| Xbox (tu cuenta) | Historial y % de logros | Clave gratuita de [OpenXBL](https://xbl.io), un servicio de terceros que no es de Microsoft |
| SteamGridDB | Portadas 2:3 (también de Nintendo y PlayStation) | Clave gratuita de la [API de SteamGridDB](https://www.steamgriddb.com/profile/preferences/api) |
| Epic Games | Juegos instalados | Nada. Epic no tiene una API pública de biblioteca |
| GOG, EA app y Ubisoft | Juegos instalados | Nada |
| RetroAchievements | Juegos retro jugados, consola, última sesión, % de logros y % en modo Hardcore | Tu usuario y tu Web API Key de [retroachievements.org › Ajustes › Claves](https://retroachievements.org/settings) |
| Emuladores (este PC) | Listas `.lpl` de RetroArch (con horas y última sesión si guarda registros de tiempo) y las carpetas de juegos de Dolphin (GameCube/Wii), PCSX2 (PS2) y DuckStation (PS1) | Nada |
| Nintendo y PlayStation | — | No ofrecen una forma de leer tu biblioteca: se añaden a mano (o con RetroAchievements / emuladores para las consolas antiguas) |
| IGDB (de Twitch) | Duración estimada: historia principal y al 100 %, con los tiempos que envían los jugadores | Client ID y secreto de una aplicación gratuita en [dev.twitch.tv › Consola › Aplicaciones](https://dev.twitch.tv/console/apps) |
| HowLongToBeat | Solo un enlace: «Ver en HowLongToBeat» abre la búsqueda del juego en tu navegador | Nada. No tiene API y su `robots.txt` y sus condiciones prohíben la consulta automática sin permiso escrito de Ziff Davis, así que la app no lee su web |

- **Duración desde IGDB:** en el inspector, «Buscar duración en IGDB» enseña hasta 3 resultados (título, año, horas
  y cuántos tiempos hay) y «Usar» escribe `{ttb: 27h / 63h}`. En «Por jugar», «Rellenar duraciones» busca los juegos que
  aún no la tienen. Solo escribe las coincidencias claras del título y deja los dudosos para revisarlos uno a uno.
  Todo queda en un solo paso de «Deshacer». Solo se envía el título del juego, y solo al pulsar.

- **Propuesta de sección:** 100 % de logros → Completados al 100%; jugado hace menos de 3 semanas →
  Jugando ahora; superado en RetroAchievements → Completados; con horas o con algún logro → Inconclusos;
  sin nada → Por jugar. Puedes cambiarla antes de añadir.
- **Juegos retro:** se proponen con la nota `[emulación]` delante de la plataforma, como en tu lista
  (`-Super Mario World [emulación] - SNES {Logros 100%} {Hardcore: 100%} {ra:228}`). El nombre de la consola se
  abrevia (SNES, PS1, GBA, Mega Drive…).
- **Nombres de ROM:** se quitan las etiquetas de región, idioma, revisión y disco — `(USA)`, `(Rev 1)`,
  `(En,Fr,De)`, `(Disc 1)`, `[!]` — y el artículo pasa delante («Legend of Zelda, The» → «The Legend of Zelda»),
  para reconocer la saga, buscar la portada y marcar los que ya tienes. Varios discos cuentan como un juego.
- **«Actualizar horas y logros de los enlazados»** refresca los datos de los juegos que tienen `{steam:…}`
  o `{ra:…}` (también el campo `{Hardcore: …}`).
- **Las claves** se guardan cifradas con tu usuario de Windows (DPAPI) en `%APPDATA%\ListaJuegos\settings.json`.
  Nunca se escriben en claro: si una versión anterior dejó alguna así, se borra al abrir la app.
- **Portadas:** están desactivadas hasta que las activas, y se guardan en `%LOCALAPPDATA%\ListaJuegos\covers`.
  - **Con clave de [SteamGridDB](https://www.steamgriddb.com):** se buscan allí primero. Son carátulas 2:3 de
    la comunidad (600×900, estáticas, sin NSFW ni humor) y también hay de juegos de Nintendo y PlayStation.
  - **Cómo conseguir la clave:** es gratuita. Inicia sesión en steamgriddb.com, ve a Preferencias › API y pégala
    en Ajustes › Cuentas. Al guardarla se comprueba y se vuelven a buscar las portadas.
  - **Portada elegida a mano:** en el campo «Portada de SteamGridDB» del panel del juego pega el número o el enlace
    (`steamgriddb.com/game/5245268` para Super Mario Odyssey). Se guarda como `{sgdb:5245268}` y manda sobre la
    búsqueda automática.
  - **Si SteamGridDB no tiene el juego,** o no hay clave, se busca en la tienda pública de Steam, sin cuenta.
  - **Si no hay portada en ningún sitio,** se muestra una carátula con el nombre del juego.
  - Las miniaturas del `.md` siguen enlazando a Steam, porque necesitan una dirección pública fija.
- **Privacidad:** la app solo se conecta cuando pulsas Importar o Actualizar, o si activas las portadas.
  RetroAchievements recibe tu usuario y tu clave solo al importar o actualizar. El escáner de emuladores
  solo lee archivos de este PC: no se conecta a nada.
  Con las portadas activadas, el título de cada juego se envía a SteamGridDB (si hay clave) y a Steam.
  No envía estadísticas a nadie.

## Accesibilidad

- Todo se maneja con el teclado (pulsa `F1` para ver los atajos): flechas para moverse por los juegos,
  `Insert` para añadir, `Supr` para eliminar, `F2` para editar, `Alt+↑/↓` para reordenar,
  `Ctrl+Tab` para cambiar de sección, `Ctrl+J` para «¿Qué juego ahora?» y `Ctrl +/−/0` para el zoom.
- Funciona con Narrador y NVDA: todos los controles tienen nombre (comprobado con UI Automation) y los
  cambios se anuncian, por ejemplo «Movido a Completados».
- El tema sigue el modo claro u oscuro de Windows. Respeta el alto contraste y «reducir movimiento».
- Anillo de foco visible, contraste de al menos 4.5:1 y nada se distingue solo por el color.
- En pantallas táctiles (ROG Ally, tabletas) todos los botones y campos miden al menos 44 px.
- **Con mando** (Xbox, PlayStation o cualquiera que Windows reconozca): cruceta o stick izquierdo para moverse por
  los juegos y por los campos del inspector, `LB`/`RB` para cambiar de sección, `A` para abrir el juego o confirmar
  la edición, `B` para cerrar el inspector o un diálogo o borrar la búsqueda, e `Y` para marcarlo como completado
  hoy. Al mantener una dirección repite con suavidad. Con el mando conectado, el pie de la ventana enseña la ayuda de
  botones y el foco se realza siempre. El mando solo actúa con la ventana de Checkpoint activa. Con «reducir
  movimiento» no hay desplazamientos animados.

## Línea de comandos

`Checkpoint.exe` también trabaja sin ventana. Sin `--file`, usa la última lista abierta en la app.

```
Checkpoint.exe --add "Hollow Knight: Silksong" --platform "Switch 2" [--section "Por jugar"] [--saga "Hollow Knight"]
Checkpoint.exe --complete "silksong"
Checkpoint.exe --stats [--json] [--file "D:\Listas\Juegos que me he pasado.txt"]
Checkpoint.exe --help
```

- `--add` va por defecto a «Por jugar» y, sin `--saga`, a los juegos sueltos. Si la saga no existe en esa sección, se
  crea. Se niega a añadir un título que ya está en la lista.
- `--complete` funciona igual que «Marcar como completado»: lleva el juego a Completados, a su misma saga, con la
  fecha de hoy si no tenía. Vale una parte del título si solo coincide con un juego.
- `--stats --json` da juegos y horas por sección, total de horas y nota media, en JSON para scripts o Stream Deck.
- Al guardar respeta el formato igual que la app: las líneas que no cambian quedan idénticas, deja la copia `.bak` y
  pone al día «(Actualizado el …)» según Ajustes. Si la lista está abierta en la ventana, esta se recarga sola.
- Códigos de salida: `0` bien · `1` error al leer o guardar · `2` argumentos incorrectos · `3` juego no encontrado,
  ambiguo o repetido.
- Es una app de ventana: en PowerShell, para esperar el resultado y leer `$LASTEXITCODE`, encadénala con una tubería,
  por ejemplo `Checkpoint.exe --stats | Out-String`. Los scripts que leen la salida ya esperan.
- Sin argumentos, o solo con la ruta de un `.txt`/`.md`, se abre la ventana como siempre.

## Para desarrolladores

```
src/ListaJuegos.Core   formato .txt/.md (TxtFormat, MdFormat, EntryLine), campos {…}, conectores, sagas
src/ListaJuegos.App    ventana WPF + WebView2; la interfaz (ui/) va dentro del .exe y se sirve en https://app.lj/
tests/                 217 pruebas xUnit (LJ_NET=1 activa las que usan la tienda real de Steam)
tools/                 pruebas de punta a punta con UI Automation, capturas, icono y sprite
```

- Compilar y probar: `dotnet test`.
- Publicar: `dotnet publish src/ListaJuegos.App -c Release -o publish`.
- La interfaz se puede abrir en un navegador con datos de prueba (`ui/index.html`, servido por http).
  `LJ_DEVTOOLS=1` activa las herramientas de desarrollo dentro de la app.
- **Diseño:** estética de biblioteca de juegos (Playnite, Backloggd, Steam).
  - Escala de elevación oscura: fondo `#0c0d10`, paneles `#13151b` y tarjetas `#1a1d26`, con un borde superior
    iluminado.
  - Acento coral solo para lo activo y para Guardar.
  - Tipografía: Outfit para los titulares, Epilogue para el texto y JetBrains Mono para números, fechas, horas,
    versión y barra de estado. Las tres son de licencia OFL y van incluidas.
  - Portadas en póster 2:3; si un juego no tiene portada, una carátula tipográfica.
  - Puntuación con 5 estrellas y medias estrellas. Por debajo son 10 radios y el archivo sigue guardando
    `{★ n/10}`.
  - Inspector con cabecera fija, bloques de campos y «Opciones avanzadas de archivo» plegables.
  - Etiquetas de plataforma con su color y fondo ambiental con la portada.
  - Transiciones con View Transitions; la raya de la pestaña activa se desliza.
  - La capa de movimiento (`vendor/motion.css`) y los iconos de marca reales en color (Steam, PlayStation,
    Epic, GOG, EA, Ubisoft) vienen de la skill `design-taste-frontend`. Xbox y Nintendo se
  escriben como texto porque la skill no tiene una fuente de iconos con licencia que lo permita. Los
  glifos de interfaz son de Lucide. Los nombres y logotipos son marcas de sus dueños.
