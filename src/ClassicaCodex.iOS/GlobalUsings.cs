// Every tool in the Mac front end derives from Window, directly or through
// FeatureWindow, and shows itself with Show or ShowDialog. An iPad has no
// windows, and constructing one throws. These lines send all of them to
// PageWindow instead, which is why the shared files need next to no changes.
//
// TopLevel goes the same way because the shared code asks for its window with
// TopLevel.GetTopLevel(this) as Window, which only compiles if the two are
// related. The lifetime goes because its MainWindow is typed as the real
// Window; nothing on iOS implements it, so the code that reads it never runs.
global using Window = ClassicaCodex.iOS.PageWindow;
global using TopLevel = ClassicaCodex.iOS.PageWindow;
global using WindowClosingEventArgs = ClassicaCodex.iOS.PageClosingEventArgs;
global using IClassicDesktopStyleApplicationLifetime = ClassicaCodex.iOS.INoDesktopLifetime;
