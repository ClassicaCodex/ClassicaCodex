Classica Codex for Linux (preview)

Run ./classica-codex from this folder. To add it to your application menu,
run ./install.sh once. Nothing else needs installing: the app carries its own
copy of .NET.

The library and settings are kept in ~/.local/share/ClassicaCodex, and a
library made on Windows or a Mac opens as it is: it is one .db file.

WHAT IT NEEDS FROM THE SYSTEM

Most desktops have all of these already:

    fontconfig, libX11, ICU

Wayland desktops run it through XWayland. On Debian and Ubuntu the packages
are fontconfig, libx11-6, and libicu (with a number after it); on Fedora,
fontconfig, libX11 and libicu.

WHAT IS OPTIONAL

Each of these turns on one thing, and the app works without any of them.

    Ancient scripts   the Noto fonts: Noto Sans Cuneiform, Noto Sans Egyptian
                      Hieroglyphs, Noto Serif Hebrew and Noto Naskh Arabic.
                      fonts-noto-core on Debian and Ubuntu; google-noto-*
                      packages on Fedora. Without them these texts show boxes.
    Greek             a serif with polytonic Greek. DejaVu Serif is used if
                      it is there (fonts-dejavu on Debian and Ubuntu).
    Read Aloud        espeak-ng
    Saving API keys   secret-tool, usually in a package called libsecret-tools,
                      and a keyring such as GNOME Keyring or KWallet. Without
                      it a key lasts until the app quits. It is never written
                      to a plain file.
    Arcade sound      pw-play, paplay or aplay
    Links and folders xdg-open
