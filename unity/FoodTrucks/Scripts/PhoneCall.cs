#nullable enable
using System;
using System.Linq;
using Entities;
using UI.Smartphone.Apps.Contacts;

namespace FoodTrucks
{
    internal static class PhoneCall
    {
        private static bool failed;

        internal static bool HangUp()
        {
            var dialog = DialogController.current;
            if (dialog == null) return true;
            var contact = dialog.contact;
            var lastBefore = contact?.messagesQueue?.LastOrDefault();
            dialog.CancelDialog();
            try
            {
                var last = contact?.messagesQueue?.LastOrDefault();
                if (contact != null && last != null && !ReferenceEquals(last, lastBefore) && last.isFromPlayer &&
                    dialog is ContactsApp app && app.isActiveAndEnabled && app.ShowMessageIfOpen(contact, last))
                    app.ScrollConversationToBottom();
            }
            catch (Exception ex)
            {
                if (!failed) Log.Error("Phone: showing the hang-up line: " + ex);
                failed = true;
            }
            return DialogController.current == null;
        }

        internal static void HangUpAction() => HangUp();
    }
}
