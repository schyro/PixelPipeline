using PixelPipeline.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PixelPipeline.Demo
{
    public sealed class PixelWeatherDemoInput : MonoBehaviour
    {
        [SerializeField] private PixelEnvironmentController environmentController;

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || environmentController == null)
                return;

            if (keyboard.digit1Key.wasPressedThisFrame)
                environmentController.SetWeather(PixelWeatherState.Sunny);
            else if (keyboard.digit2Key.wasPressedThisFrame)
                environmentController.SetWeather(PixelWeatherState.Cloudy);
            else if (keyboard.digit3Key.wasPressedThisFrame)
                environmentController.SetWeather(PixelWeatherState.Rain);

            if (keyboard.tKey.wasPressedThisFrame)
                environmentController.TimePassing = !environmentController.TimePassing;
        }
    }
}
