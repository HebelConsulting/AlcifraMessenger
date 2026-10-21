#import "template.typ": conf, note, accent, idx, index-page

#show: doc => conf(doc)

// ─────────────────────────────────────────────────────────────────────────────
// Title page
// ─────────────────────────────────────────────────────────────────────────────
#page(numbering: none, footer: none)[
  #set align(center + horizon)
  #block[
    #text(size: 34pt, weight: "bold", fill: accent)[SimplArchive]
    #v(0.2em)
    #text(size: 18pt)[Messenger — Module Manual]
    #v(1.2em)
    #text(size: 11pt, fill: gray)[
      Conversations between the people of one archive, \
      sealed on their devices to keys only they hold
    ]
  ]

  #place(bottom + center)[
    #set text(size: 8.5pt, fill: gray)
    #align(center)[
      Generated #sys.inputs.at("generated", default: "from source") \
      © #datetime.today().year() \
      Hebel Consulting GmbH \
      Schweighofplatz 7 \
      6010 Kriens (LU) \
      Switzerland \
      #link("mailto:support@simplarchive.dev")[support\@simplarchive.dev]
    ]
  ]
]

// The contents are front matter: unnumbered, with the count starting at 1 on the Introduction.
#set page(numbering: none)
#outline(title: "Contents", depth: 1, indent: auto)
#counter(page).update(0)
#set page(numbering: "1")

// ─────────────────────────────────────────────────────────────────────────────
= Introduction

The *Messenger*#idx("Messenger") lets the people of one archive talk to each other: one-to-one, or in a named group,
with messages, photos and files. It is a *closed user group*#idx("closed user group"): only users of the same tenant
take part, and only those with an enrolled key.

Everything a member writes or sends is *sealed on their own device*#idx("sealed") to the keys of the conversation's
members, and opened only on a member's device. The server stores and forwards what it cannot read.

The Messenger is a *free, open-source module*: it needs no licence, and a tenant administrator activates it for the
tenant. Its clients are the SimplArchive apps: the
phone app's Messages tile comes first (in preparation), the desktop client next. The web client stays out, because a
browser cannot reach a hardware key.

== Who can take part

A member is a user of the tenant with *at least one enrolled certificate*#idx("enrolled certificate") whose key they
hold — a smart card, a security key, a phone's key. Nothing can be sealed to someone without one, so they cannot be
added, and the people picker does not offer them.

While the Messenger is active, the archive *offers certificate enrolment* to its users, and the
certificates they enrol are the ones conversations are sealed to.

// ─────────────────────────────────────────────────────────────────────────────
= Setting it up

+ *Activate the module* for the tenant — no licence is needed, and it never lapses. This repository's standalone start
  (`demo/up.sh`) activates it by itself.
+ *Choose where conversations are filed.* The module's setting _Where conversations are filed_#idx("repository
  setting") takes the id of a repository or folder. Group conversations and the shared folders of one-to-one chats are
  created there. Give it an administrator's rights only: each member gets access to their own conversations and
  nothing else, and the Messenger keeps those rights in step with each conversation's members.
+ *Let people enrol.* Members need an enrolled certificate before anyone can start a conversation with them.
+ *Decide whether files may leave the app.* The setting _Keep files in the app_#idx("Keep files in the app") is off by
  default: a file the app cannot show can then be handed to another app, deliberately and after a warning. Turned on,
  files open only in the app and nothing hands them on (see _Photos and files_).

```sh
saconsole module activate --module messenger
echo <repository id> | saconsole module settings set messenger messenger.repositoryId --stdin
echo true | saconsole module settings set messenger messenger.filesStayInApp --stdin   # optional
```

Every user's personal space gets a folder *My Messages*#idx("My Messages"), provisioned by the archive while the module
is active. The module may act only on that folder of the personal space, never on the rest of it.

// ─────────────────────────────────────────────────────────────────────────────
= Conversations

== One-to-one

There is *one* conversation per pair of people, created the first time either starts it. Its shared folder sits in the
configured repository; in each person's *My Messages* a folder named after the other person leads to it.

== Groups

A *group*#idx("group") has a name and starts with its creator as its *administrator*#idx("administrator"). Administrators
add members, remove them, and make other members administrators or take that back. Anyone may leave.

A group always keeps *at least one administrator*: the last one cannot step down, nor leave while others remain, until
they have made someone else one. The administrators are listed on the group's folder, so the archive shows them too.

When the last member leaves, the conversation is removed, its folder and files with it.

== Keys and epochs

Each conversation has a *conversation key*#idx("conversation key"), and a new one for every *epoch*#idx("epoch"). A
member holds the key of an epoch as a copy wrapped to their certificates.

- *Membership is the current epoch's keys.* Whoever holds a key for the current epoch, and has not left, is a member;
  the folder's access list follows.
- *Removing someone starts a new epoch.* An administrator's device makes a new key and wraps it to the members who
  remain, so the removed member cannot read what follows.
- *Leaving does too — made by someone who stays.* The person leaving makes no key, because they would then know it.
  Until a remaining member's app publishes the next epoch, nothing more is sent; the app does that by itself before its
  next message.
- *What you were in stays yours.* After leaving or being removed you keep your own keys for the stretch you were a
  member: the conversation stays in your list, marked *left*, as history to read and to back up — and nothing sent
  after you left, which is sealed under an epoch you hold no key for.
- *Adding someone starts a new epoch as well.* The administrator's device makes a new key for everyone, the newcomer
  included, who therefore reads from joining on and holds nothing that opens the history before it — nor is it listed
  to them.

The apps say both plainly when it happens.

// ─────────────────────────────────────────────────────────────────────────────
= Messages

- *Send.* The device seals the message under the current epoch's key before it leaves; the server refuses a message
  sealed under an older epoch, so a member who missed a removal fetches the new key first.
- *Edit.* The sender replaces the text; the message shows as edited. Only the sender can, and only if nobody changed
  it since they read it.
- *Delete for everyone.* The sender removes the text; the message's place in the thread stays, marked deleted.
- *Delete for me.* Hides the message for you only.
- *Read markers*#idx("read marker") only move forward, so an older device catching up changes nothing.

== Read receipts

Beneath your latest message that somebody has read, the thread shows *"Read"* in a one-to-one chat, or *"Read by 2 of
4"* in a group; tapping that message and choosing *Read by…* lists who. A receipt is how far each member has read the
conversation, not a mark per message, and it is always on: the same marker that counts your unread messages and
clears the notification is what the others see.

== Typing

While someone types in a conversation you have open, the thread says so beneath the messages: *"Anna is typing…"*,
*"Anna and Tom are typing…"*, or in a busy group *"Several people are typing…"*. It goes away when they send, stop, or
after a few seconds without a keystroke.

It is *on by default* and works *both ways*#idx("typing indicator"): switched off under *Messenger settings* in the
conversation list's menu, the others no longer see when you type, and you no longer see when they do. The setting is
yours alone and follows you to every device.

Only an app that is open hears it: nothing is stored, nothing is pushed to a closed app. The server learns *that*
someone types, and where — never what.

== The "New messages" notification

When a message arrives, every other member gets a notification that says *"New messages"* — never the sender, never the
text, because it can reach a lock screen. It is *never mailed*. While it is unread, further messages fold into it
rather than adding more, so the phone is pushed once per burst; reading the conversation clears it, and the next
message pushes again.

The push itself carries nothing but the notification's id; the phone fetches the rest from the archive. Tapping it
opens that conversation, the key dialog first if the key is needed.

// ─────────────────────────────────────────────────────────────────────────────
= Photos and files

A photo or file is sealed on the device like a message and uploaded straight to the archive's storage. It is filed
once, as a *Message Object*#idx("Message Object") in the conversation's folder, and each member finds it through their
own chat folder.

Files are added to the message being written: each appears as a chip above the text field, with its name and size, and
✕ removes it. Several files go in one message, with whatever is typed as their caption; the text is optional.

- *Only sealed content is accepted.* The archive checks that every Message Object is a sealed envelope and refuses
  anything else, and hands sealed files out on every encryption tier.
- *Kept apart from the archive.* Message Objects live in a storage area of their own and are left out of search,
  previews, text recognition, retention schedules and legal holds: the archive cannot read them, and their lifetime is
  the conversation's.
- *Delete for me* removes your reference; *delete for everyone* removes the file for all.

*Opening a file keeps it in the app*#idx("opening a file") wherever the app can show it, because a file handed to
another app is a readable copy the Messenger no longer protects. Photos, PDFs and plain text, video and sound open in
the app, from memory: nothing of them is written to the device's storage. Anything else, an office document say, can
only be handed on: never by a tap, always from the menu (*Open with…* or *Share*), and only after a warning that the
file leaves the app unsealed. Where the tenant keeps files in the app, the menu offers neither, and a file the app
cannot show says so and stays closed.

// ─────────────────────────────────────────────────────────────────────────────
= Backing up and restoring

A *backup*#idx("backup") holds all of your conversations in one file, *sealed only*: the messages as they were
delivered, your own sealed copy of each conversation key, and each file as the envelope it was sent as. Nothing is
decrypted to make it, so it opens only with a key whose certificate it was sealed to.

```
saconsole messenger backup --out conversations.simplarchive-messages
saconsole messenger restore --file conversations.simplarchive-messages
```

- *Restoring on another installation.* Each conversation comes back as *history*, marked *Restored* and read-only: you
  read it with your own key, nothing more is sent into it. Its messages carry a small *restored* mark, because the
  format has no sender signatures — they are as trustworthy as whoever restored them.
- *Only your own.* A backup is restored by the person it belongs to: the installation checks that its owner's e-mail is
  yours.
- *Twice changes nothing.* Messages keep their ids; what is already there is skipped. A second member restoring the
  same conversation joins it rather than making another.
- *Checked first.* Every entry is checked against the backup's own list of hashes before anything is restored; a
  changed or damaged backup is refused as a whole.
- *Continue here.* When everyone from a restored conversation is on this installation with an enrolled key (matched
  by e-mail), its thread offers *Continue here*. A one-to-one chat continues as the chat with that person — the one
  already started here, if there is one — and a group as a new group of the same people, administered by whoever
  continued it, with a new key for everyone. The restored history then sits above the live conversation; members who
  did not restore it see the conversation from there on, never history their own keys did not bring. Where somebody
  is missing, the thread names them and the conversation stays history.
- *Bound to your keys at the time.* A key enrolled only later cannot open older content; a renewed certificate on the
  same key can. A second enrolled key — a spare card — is your insurance.

// ─────────────────────────────────────────────────────────────────────────────
= What the server can see

#table(
  columns: (1fr, 1fr),
  align: (left, left),
  table.header([*The server sees*], [*The server cannot see*]),
  [who is in which conversation, and who administers a group], [the text of any message],
  [who sends when, and how large each message and file is], [the content of any photo or file],
  [read markers, edits and deletions — that they happened], [any conversation key],
  [that someone is typing, and in which conversation], [what they are typing],
  [a group's name, and the names on chat folders], [],
)

#note[*No forward secrecy*#idx("forward secrecy") in this version. Messages are sealed to the members' certificates, as
sealed documents are. Someone who obtains a member's private key *and* the stored messages can read that member's past
conversations. Keep keys on hardware that does not let them out.]

// ─────────────────────────────────────────────────────────────────────────────
= The format

For those who audit it:

- A random 256-bit *conversation key* per epoch, wrapped by a sealed envelope to *every* certificate of every member — a
  member with several cards or devices opens it with any of them.
- Messages, photos and files are encrypted with that key using AES-256-GCM, with the format version, the conversation,
  the epoch, the message id and the sender bound into the authentication, so a message cannot be replayed into another
  conversation, epoch or sender.
- The client library and the key library are open source, in the Messenger's public repository.

// ─────────────────────────────────────────────────────────────────────────────
= Not yet

- *Disappearing messages* are not offered.

#index-page()
