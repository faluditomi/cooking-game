using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Cooking.Prototype
{
    /// <summary>
    /// The pause menu (Esc, or the pause button over the market): Resume, Main Menu, Settings and
    /// Quit Game. Pausing stops the game's time (timers, cooking, customers, dialogue). Settings has
    /// the sound effects and music volumes, kept between sessions; the title screen opens it too.
    /// </summary>
    public partial class CookingPrototype
    {
        private const string SfxVolumeKey = "Cooking.SfxVolume";
        private const string MusicVolumeKey = "Cooking.MusicVolume";

        private GameObject pauseOverlay;
        private GameObject pauseMenu;
        private GameObject settingsPanel;
        private Slider sfxSlider;
        private Slider musicSlider;
        private Button pauseButton;
        private bool paused;
        /// <summary>Where Settings' Back goes: the pause menu, or the title screen.</summary>
        private bool settingsFromTitle;

        /// <summary>The player's sound effects volume (0..1), on top of the mix in the inspector.</summary>
        private float playerSfx = 1f;
        /// <summary>The player's music volume (0..1).</summary>
        private float playerMusic = 0.8f;

        private void LoadVolumes()
        {
            playerSfx = PlayerPrefs.GetFloat(SfxVolumeKey, 1f);
            playerMusic = PlayerPrefs.GetFloat(MusicVolumeKey, 0.8f);
        }

        /// <summary>Esc opens and closes the pause menu during a game.</summary>
        private void UpdatePauseKey()
        {
            Keyboard keyboard = Keyboard.current;
            if(keyboard == null || !keyboard.escapeKey.wasPressedThisFrame) return;

            if(settingsPanel.activeSelf)
            {
                CloseSettings();
                return;
            }

            if(titleOverlay.activeSelf) return;
            if(paused) Resume();
            else Pause();
        }

        private void Pause()
        {
            if(paused || titleOverlay.activeSelf) return;

            // Whatever was in hand goes back where it came from.
            DropWithoutEffect();

            paused = true;
            Time.timeScale = 0f;
            pauseOverlay.SetActive(true);
            pauseMenu.SetActive(true);
            settingsPanel.SetActive(false);
            pauseOverlay.transform.SetAsLastSibling();
        }

        private void Resume()
        {
            paused = false;
            Time.timeScale = 1f;
            pauseOverlay.SetActive(false);
        }

        private void PauseToMainMenu()
        {
            Resume();
            DialogueSystem.Dialogue.StopAll();
            // Starting from the title begins a fresh game.
            gameOver = true;
            ShowTitle();
        }

        private void OpenSettings(bool fromTitle)
        {
            settingsFromTitle = fromTitle;
            sfxSlider.SetValueWithoutNotify(playerSfx);
            musicSlider.SetValueWithoutNotify(playerMusic);
            pauseOverlay.SetActive(true);
            pauseOverlay.transform.SetAsLastSibling();
            pauseMenu.SetActive(false);
            settingsPanel.SetActive(true);
        }

        private void CloseSettings()
        {
            PlayerPrefs.Save();
            settingsPanel.SetActive(false);

            if(settingsFromTitle)
            {
                pauseOverlay.SetActive(false);
                return;
            }

            pauseMenu.SetActive(true);
        }

        private void SetSfxVolume(float value)
        {
            playerSfx = value;
            PlayerPrefs.SetFloat(SfxVolumeKey, value);
            ApplyVolumes();
            Play(sounds.uiClick, 0.6f, 0f);
        }

        private void SetMusicVolume(float value)
        {
            playerMusic = value;
            PlayerPrefs.SetFloat(MusicVolumeKey, value);
            ApplyVolumes();
        }

        // ------------------------------------------------------------------ UI

        private void BuildPause(Transform root)
        {
            // A small pause button in the market's top-left corner, for the mouse.
            pauseButton = UIFactory.Button(root, "II", Pause, null, 18);
            RectTransform buttonRect = (RectTransform)pauseButton.transform;
            buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(ConversationWidth, 1f);
            buttonRect.pivot = new Vector2(0f, 1f);
            buttonRect.sizeDelta = new Vector2(44f, 40f);
            buttonRect.anchoredPosition = new Vector2(8f, -8f);

            Image dim = UIFactory.Panel(root, "Pause", new Color(0.04f, 0.035f, 0.05f, 0.82f));
            UIFactory.Stretch(dim.rectTransform);
            pauseOverlay = dim.gameObject;

            // The menu.
            RectTransform menu = UIFactory.Panel(dim.transform, "Pause Menu", UIFactory.PanelColor).rectTransform;
            menu.anchorMin = menu.anchorMax = new Vector2(0.5f, 0.5f);
            VerticalLayoutGroup layout = UIFactory.Vertical(menu.gameObject, 14f, 28);
            layout.childForceExpandHeight = false;
            // As tall as its buttons (the browser build has no Quit).
            menu.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            menu.sizeDelta = new Vector2(440f, 0f);
            pauseMenu = menu.gameObject;

            Text title = UIFactory.Label(menu, "Paused", 40, TextAnchor.MiddleCenter, FontStyle.Bold, UIFactory.Accent);
            UIFactory.Size(title, 60f, 0f);
            UIFactory.Button(UIFactory.Row(menu, 52f), "Resume", Resume, Orange, 24);
            UIFactory.Button(UIFactory.Row(menu, 46f), "Main Menu", PauseToMainMenu, null, 20);
            UIFactory.Button(UIFactory.Row(menu, 46f), "Settings", () => OpenSettings(false), null, 20);
#if !UNITY_WEBGL
            UIFactory.Button(UIFactory.Row(menu, 46f), "Quit Game", QuitGame, new Color(0.35f, 0.2f, 0.2f, 1f), 20);
#endif

            // Settings: the two volumes.
            RectTransform settings = UIFactory.Panel(dim.transform, "Settings", UIFactory.PanelColor).rectTransform;
            settings.anchorMin = settings.anchorMax = new Vector2(0.5f, 0.5f);
            VerticalLayoutGroup settingsLayout = UIFactory.Vertical(settings.gameObject, 14f, 28);
            settingsLayout.childForceExpandHeight = false;
            settings.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            settings.sizeDelta = new Vector2(520f, 0f);
            settingsPanel = settings.gameObject;

            Text settingsTitle = UIFactory.Label(settings, "Settings", 36, TextAnchor.MiddleCenter, FontStyle.Bold, UIFactory.Accent);
            UIFactory.Size(settingsTitle, 54f, 0f);
            sfxSlider = VolumeRow(settings, "Sound effects", SetSfxVolume);
            musicSlider = VolumeRow(settings, "Music", SetMusicVolume);
            UIFactory.Button(UIFactory.Row(settings, 48f), "Back", CloseSettings, Orange, 22);

            settingsPanel.SetActive(false);
            pauseOverlay.SetActive(false);
        }

        /// <summary>A labelled 0..1 slider.</summary>
        private static Slider VolumeRow(RectTransform parent, string label, UnityEngine.Events.UnityAction<float> changed)
        {
            RectTransform row = UIFactory.Row(parent, 44f, 16f);
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            Text text = UIFactory.Label(row, label, 20, TextAnchor.MiddleLeft);
            UIFactory.Size(text, 44f, 0f, 170f, 0f);

            RectTransform sliderRect = UIFactory.Rect(row, "Slider");
            UIFactory.Size(sliderRect, 44f, 0f, -1f, 1f);
            Slider slider = sliderRect.gameObject.AddComponent<Slider>();

            Image track = UIFactory.Panel(sliderRect, "Track", new Color(0.08f, 0.08f, 0.09f, 1f));
            UIFactory.SkinBar(track, new Color(0.2f, 0.2f, 0.23f, 1f));
            UIFactory.Place(track.rectTransform, 0f, 0.38f, 1f, 0.62f);

            RectTransform fillArea = UIFactory.Rect(sliderRect, "Fill Area");
            UIFactory.Place(fillArea, 0f, 0.38f, 1f, 0.62f);
            Image fill = UIFactory.Panel(fillArea, "Fill", UIFactory.Accent);
            UIFactory.SkinBar(fill, UIFactory.Accent);
            fill.rectTransform.sizeDelta = Vector2.zero;

            RectTransform handleArea = UIFactory.Rect(sliderRect, "Handle Area");
            UIFactory.Stretch(handleArea);
            handleArea.offsetMin = new Vector2(10f, 0f);
            handleArea.offsetMax = new Vector2(-10f, 0f);
            Image handle = UIFactory.Panel(handleArea, "Handle", UIFactory.TextColor);
            handle.sprite = UIFactory.Circle;
            handle.rectTransform.sizeDelta = new Vector2(24f, 0f);

            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.onValueChanged.AddListener(changed);
            return slider;
        }
    }
}
