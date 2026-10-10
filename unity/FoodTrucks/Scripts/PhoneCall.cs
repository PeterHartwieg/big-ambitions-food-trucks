#nullable enable
using System;
using System.Linq;
using Entities;
using UI.Smartphone.Apps.Contacts;

namespace FoodTrucks
{
    // Hang up for the mod's phone calls (commissary, bookings, City). The game's own hang-up (DialogController.CancelDialog)
    // ends the call and stores the player's "Never mind, I'm no longer interested" in the contact's history with
    // Contact.ReceivePlayerMessage, which shows nothing in the open conversation: the line appears only when the
    // conversation is opened again, and the call's last buttons stay, greyed, as they do for every earlier step.
    // That reads as a call still going (0.9.7 playtest, step 19), so the line is also shown in the open conversation,
    // through the app's own ShowMessageIfOpen, as the game does for texts it sends during a call.
    internal static class PhoneCall
    {
        private static bool failed;

        // Returns whether the call is over (the phone's CALL button usable again), for the caller's log line.
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

        // For a DialogEntry's OnCancel.
        internal static void HangUpAction() => HangUp();
    }
}
