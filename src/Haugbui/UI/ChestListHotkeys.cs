using System.Collections.Generic;
using Haugbui.Core.Persistence;
using Haugbui.Core.Storage;
using Haugbui.Integration;
using Haugbui.Structures;
using UnityEngine;

namespace Haugbui.UI
{
    /// <summary>
    /// Shift+C and Shift+V on the chest under the crosshair. C walks and V picks up, so those presses are swallowed.
    /// </summary>
    internal static class ChestListHotkeys
    {
        public static string HoverKeys(string copyToken, string pasteToken) =>
            $"\n[<color=yellow><b>Shift+C</b></color>] {copyToken}\n[<color=yellow><b>Shift+V</b></color>] {pasteToken}";

        public static void Update()
        {
            if (ChestFilterPanel.IsOpen || DeliveryPanel.IsOpen) return;
            bool copy = PanelInput.CopyPressed();
            bool paste = !copy && PanelInput.PastePressed();
            if (!copy && !paste) return;
            Container container = Aimed();
            if (container == null) return;
            if (copy) Copy(container);
            else Paste(container);
        }

        public static bool AimedAtList() => Aimed() != null;

        public static void Copy(Container container)
        {
            if (!Eligible(container)) return;
            DeliveryChest delivery = container.GetComponent<DeliveryChest>();
            if (delivery != null)
            {
                CopyRequests(delivery);
                return;
            }
            ZNetView view = container.m_nview;
            PanelClipboard.CopyFilters(ChestFilterStore.Read(view != null && view.IsValid() ? view.GetZDO() : null));
        }

        public static void Paste(Container container)
        {
            if (!Eligible(container)) return;
            DeliveryChest delivery = container.GetComponent<DeliveryChest>();
            if (delivery != null)
            {
                PasteRequests(delivery);
                return;
            }
            if (!PanelClipboard.HasFilters) return;
            ZNetView view = container.m_nview;
            if (view == null || !view.IsValid()) return;
            if (!view.IsOwner()) view.ClaimOwnership();
            ChestFilterStore.Write(view.GetZDO(), PanelClipboard.FilterLines());
        }

        private static void CopyRequests(DeliveryChest chest)
        {
            var lines = new List<RequestDraft>();
            foreach (DeliveryRequest line in chest.Ledger.Lines)
            {
                if (line.Requested <= 0 || line.Requested > DeliveryLedger.MaxRequested) return;
                lines.Add(new RequestDraft(line.ItemPrefab, line.Requested, line.KeepAmount));
            }
            PanelClipboard.CopyRequests(lines);
        }

        private static void PasteRequests(DeliveryChest chest)
        {
            if (chest.ReadOnly || !PanelClipboard.HasRequests) return;
            ZNetView view = chest.View;
            if (view != null && view.IsValid() && !view.IsOwner()) view.ClaimOwnership();
            RequestEditResult result = RequestEditResult.Ok;
            bool saved = chest.Edit(ledger => (result = ledger.Replace(PanelClipboard.Requests)) == RequestEditResult.Ok);
            if (saved && result == RequestEditResult.Ok) return;
            string error = result == RequestEditResult.InvalidAmount ? "$haugbui_delivery_err_amount"
                : result == RequestEditResult.TooManyLines ? "$haugbui_delivery_err_full"
                : "$haugbui_delivery_err_item";
            Player.m_localPlayer?.Message(MessageHud.MessageType.Center, Localization.instance.Localize(error));
        }

        private static Container Aimed()
        {
            Player player = Player.m_localPlayer;
            if (player == null || !player.TakeInput()) return null;
            GameObject hover = player.GetHoverObject();
            Container container = hover != null ? hover.GetComponentInParent<Container>() : null;
            return Eligible(container) ? container : null;
        }

        private static bool Eligible(Container container)
        {
            if (container == null) return false;
            if (container.GetComponent<DeliveryChest>() != null) return Allowed(container);
            if (container.GetComponent<HaugbuiStructure>() != null) return false;
            if (container.GetComponent<Piece>() == null) return false;
            return Allowed(container);
        }

        private static bool Allowed(Container container)
        {
            if (container.m_checkGuardStone && !PrivateArea.CheckAccess(container.transform.position, 0f, flash: false)) return false;
            long id = Game.instance != null && Game.instance.GetPlayerProfile() != null
                ? Game.instance.GetPlayerProfile().GetPlayerID()
                : 0L;
            return container.CheckAccess(id);
        }
    }
}
