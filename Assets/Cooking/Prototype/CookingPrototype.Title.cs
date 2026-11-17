using UnityEngine;
using UnityEngine.UI;

namespace Cooking.Prototype
{
    /// <summary>
    /// The main menu: shown when the game starts and after a game over. Start begins a game (a fresh
    /// one after a game over); Quit closes the build.
    /// </summary>
    public partial class CookingPrototype
    {
        private GameObject titleOverlay;
        /// <summary>True once a game has been played, so Start resets everything first.</summary>
        private bool titleNeedsReset;

        private void ShowTitle()
        {
            titleNeedsReset = gameOver;
            overlayOpen = true;
            titleOverlay.SetActive(true);
            titleOverlay.transform.SetAsLastSibling();
        }

        private void StartGame()
        {
            StartGame(false);
        }

        private void StartGame(bool withTutorial)
        {
            titleOverlay.SetActive(false);
            overlayOpen = false;

            if(titleNeedsReset)
            {
                titleNeedsReset = false;
                RestartGame();
            }

            // The first cycle, guided.
            if(withTutorial) StartTutorial();
        }

        private static void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void BuildTitle(Transform root)
        {
            Image overlay = UIFactory.Panel(root, "Title", UIFactory.Background);
            UIFactory.Stretch(overlay.rectTransform);
            titleOverlay = overlay.gameObject;

            RectTransform panel = UIFactory.Rect(overlay.transform, "Menu");
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(900f, 640f);
            VerticalLayoutGroup layout = UIFactory.Vertical(panel.gameObject, 18f, 0);
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.MiddleCenter;

            Text title = UIFactory.Label(panel, "Emphis's Stall", 84, TextAnchor.MiddleCenter, FontStyle.Bold, UIFactory.Accent);
            UIFactory.Size(title, 120f, 0f);
            Text subtitle = UIFactory.Label(panel,
                "A street chef in the Bright Market of Erlin's Eye.\n<size=22><color=#a6a199>Cook for the regulars, get to know them, and keep the stall going.</color></size>",
                28, TextAnchor.MiddleCenter, FontStyle.Italic, UIFactory.TextColor);
            UIFactory.Size(subtitle, 110f, 0f);

            RectTransform startRow = UIFactory.Row(panel, 64f);
            UIFactory.Button(startRow, "Play with tutorial", () => StartGame(true), new Color(0.75f, 0.42f, 0.15f, 1f), 30);
            RectTransform playRow = UIFactory.Row(panel, 52f);
            UIFactory.Button(playRow, "Play", () => StartGame(false), null, 24);
#if !UNITY_WEBGL
            // A browser tab can't be quit from the game, so WebGL has no Quit.
            RectTransform quitRow = UIFactory.Row(panel, 48f);
            UIFactory.Button(quitRow, "Quit", QuitGame, null, 22);
#endif

            titleOverlay.SetActive(false);
        }
    }
}
