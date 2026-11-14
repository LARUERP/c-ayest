using System;
using BoneLib.BoneMenu;
using MelonLoader;
using UnityEngine;

namespace FusionDarkRP
{
    /// <summary>
    /// Fabrique typée pour BoneMenu. Remplace l'ancienne réflexion
    /// (CreatePage/CreateFunction via MethodInfo) par les appels directs
    /// BoneLib.BoneMenu.Page, déjà utilisés par PhoneMod.
    /// </summary>
    public static class MenuKit
    {
        public static Page NewPage(Page parent, string name, Color color)
        {
            if (parent == null)
                return null;

            return parent.CreatePage(name, color, 0, true);
        }

        public static void AddFunction(
            Page page, string label, Color color, Action action)
        {
            if (page == null || action == null)
                return;

            page.CreateFunction(label, color, action);
        }

        public static void AddInfo(
            Page page, string label, Color color, string tag)
        {
            if (page == null)
                return;

            AddFunction(page, label, color,
                () => MelonLogger.Msg(tag + " " + label));
        }
    }
}
