using System.Globalization;

namespace SimplArchive.Messenger.Views;

/// <summary>
/// The screens' texts in English, German, Italian and Spanish — the languages the SimplArchive apps speak — chosen by the
/// current UI culture, English otherwise. Self-contained, so the standalone app needs nothing else.
/// </summary>
public static class ViewText
{
    private static readonly Dictionary<string, (string En, string De, string It, string Es)> Texts = new()
    {
        ["Messages"] = ("Messages", "Nachrichten", "Messaggi", "Mensajes"),
        ["NewChat"] = ("New chat", "Neuer Chat", "Nuova chat", "Nuevo chat"),
        ["NewGroup"] = ("New group", "Neue Gruppe", "Nuovo gruppo", "Nuevo grupo"),
        ["Refresh"] = ("Refresh", "Aktualisieren", "Aggiorna", "Actualizar"),
        ["NoConversations"] = ("No conversations yet.", "Noch keine Unterhaltungen.", "Ancora nessuna conversazione.", "Todavía no hay conversaciones."),
        ["GroupMembers"] = ("Group · {0} members", "Gruppe · {0} Mitglieder", "Gruppo · {0} membri", "Grupo · {0} miembros"),
        ["Leave"] = ("Leave", "Verlassen", "Esci", "Salir"),
        ["LeaveConfirm"] = ("Leave this group? You will not read what is written after you leave.",
            "Diese Gruppe verlassen? Was danach geschrieben wird, können Sie nicht mehr lesen.",
            "Uscire da questo gruppo? Non leggerai ciò che verrà scritto dopo.",
            "¿Salir de este grupo? No podrá leer lo que se escriba después."),
        ["Yes"] = ("Yes", "Ja", "Sì", "Sí"),
        ["No"] = ("No", "Nein", "No", "No"),
        ["Cancel"] = ("Cancel", "Abbrechen", "Annulla", "Cancelar"),
        ["OK"] = ("OK", "OK", "OK", "Aceptar"),
        ["Send"] = ("Send", "Senden", "Invia", "Enviar"),
        ["TypeMessage"] = ("Message", "Nachricht", "Messaggio", "Mensaje"),
        ["Reply"] = ("Reply", "Antworten", "Rispondi", "Responder"),
        ["ReplyingTo"] = ("Replying to {0}", "Antwort an {0}", "Risposta a {0}", "Respuesta a {0}"),
        ["Edit"] = ("Edit", "Bearbeiten", "Modifica", "Editar"),
        ["DeleteForEveryone"] = ("Delete for everyone", "Für alle löschen", "Elimina per tutti", "Eliminar para todos"),
        ["DeleteForMe"] = ("Delete for me", "Für mich löschen", "Elimina per me", "Eliminar para mí"),
        ["Deleted"] = ("This message was deleted.", "Diese Nachricht wurde gelöscht.", "Questo messaggio è stato eliminato.", "Este mensaje fue eliminado."),
        ["CannotOpen"] = ("This message cannot be opened.", "Diese Nachricht lässt sich nicht öffnen.", "Questo messaggio non può essere aperto.", "Este mensaje no se puede abrir."),
        ["Edited"] = ("edited", "bearbeitet", "modificato", "editado"),
        ["You"] = ("You", "Sie", "Tu", "Usted"),
        ["Search"] = ("Name or e-mail", "Name oder E-Mail", "Nome o e-mail", "Nombre o correo"),
        ["PeopleHint"] = ("Only colleagues with an enrolled key are listed: nothing can be sealed to anyone without one, so they cannot take part.",
            "Aufgeführt sind nur Kolleginnen und Kollegen mit eingeschriebenem Schlüssel: Für alle anderen lässt sich nichts versiegeln, daher können sie nicht teilnehmen.",
            "Sono elencati solo i colleghi con una chiave registrata: per chi non ne ha una non si può sigillare nulla, quindi non può partecipare.",
            "Solo aparecen los colegas con una clave inscrita: para quien no la tiene no se puede sellar nada, así que no puede participar."),
        ["GroupName"] = ("Group name", "Gruppenname", "Nome del gruppo", "Nombre del grupo"),
        ["Create"] = ("Create", "Erstellen", "Crea", "Crear"),
        ["ChangedElsewhere"] = ("Somebody changed this meanwhile; the conversation has been reloaded.",
            "Jemand hat dies inzwischen geändert; die Unterhaltung wurde neu geladen.",
            "Qualcuno l'ha modificato nel frattempo; la conversazione è stata ricaricata.",
            "Alguien lo cambió mientras tanto; la conversación se ha recargado."),
        ["Failed"] = ("Something went wrong", "Etwas ist schiefgegangen", "Qualcosa è andato storto", "Algo salió mal"),
        ["NotAvailable"] = ("Messages are not available for you here.", "Nachrichten sind hier für Sie nicht verfügbar.",
            "I messaggi non sono disponibili per te qui.", "Los mensajes no están disponibles para usted aquí."),
    };

    /// <summary>The text for <paramref name="key"/> in the current UI language, formatted with <paramref name="args"/>.</summary>
    public static string Get(string key, params object[] args)
    {
        var texts = Texts[key];
        var text = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName switch
        {
            "de" => texts.De,
            "it" => texts.It,
            "es" => texts.Es,
            _ => texts.En,
        };
        return args.Length == 0 ? text : string.Format(CultureInfo.CurrentCulture, text, args);
    }

    /// <summary>Every key, for the test that each has all four languages.</summary>
    public static IEnumerable<string> Keys => Texts.Keys;
}
