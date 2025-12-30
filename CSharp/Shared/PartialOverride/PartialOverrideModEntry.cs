using Barotrauma;

namespace PartialItemOverride
{
    /// <summary>
    /// Пример точки входа для инициализации системы частичного переопределения.
    /// Интегрируйте это в существующий ACsMod вашего мода.
    /// </summary>
    public class PartialOverrideModEntry
    {
        /// <summary>
        /// Вызовите это из конструктора вашего основного мод-класса.
        /// Например:
        /// 
        /// public class YourMod : ACsMod
        /// {
        ///     public YourMod()
        ///     {
        ///         PartialOverrideModEntry.Initialize("com.yourname.yourmod");
        ///     }
        /// }
        /// </summary>
        /// <param name="modHarmonyId">Уникальный Harmony ID вашего мода</param>
        public static void Initialize(string modHarmonyId)
        {
            // Инициализировать систему частичного переопределения
            PartialItemOverrideSystem.Initialize(modHarmonyId);
            
            DebugConsole.NewMessage(
                "[PartialOverride] Integration initialized! " +
                "You can now use inherit=\"true\" in your item XMLs.", 
                Microsoft.Xna.Framework.Color.Cyan
            );
        }
    }
}
