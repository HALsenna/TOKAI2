using UnityEngine;

namespace Daidatsugoku
{
    // Shared, bundled font: Web players cannot depend on fonts installed on the OS.
    public static class GameFont
    {
        public static Font Load() => Resources.Load<Font>("Fonts/NotoSansJP-Regular");
    }
}
