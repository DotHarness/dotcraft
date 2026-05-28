//! Embeds the Windows application icon into `dotcraft-tui.exe` (Explorer, taskbar).
//! Keep `resources/icon.ico` in sync with `desktop/resources/icon.ico`.

fn main() {
    let target = std::env::var("TARGET").unwrap_or_default();
    if !target.contains("windows") {
        return;
    }

    let mut res = winres::WindowsResource::new();
    res.set_icon("resources/icon.ico");
    res.compile().expect("failed to compile Windows resources (icon embed)");
}
