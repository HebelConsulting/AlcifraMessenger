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

The Messenger is a *separately licensed module*, activated per tenant. Its clients are the SimplArchive apps: the
phone app's Messages tile comes first (in preparation), the desktop client next. The web client stays out, because a
browser cannot reach a hardware key.

== Who can take part

A member is a user of the tenant with *at least one enrolled certificate*#idx("enrolled certificate") whose key they
hold — a smart card, a security key, a phone's key. Nothing can be sealed to someone without one, so they cannot be
added, and the people picker does not offer them.

While the Messenger is active, it *licenses certificate enrolment*: the archive offers enrolment to its users, and the
certificates they enrol are the ones conversations are sealed to.

// ─────────────────────────────────────────────────────────────────────────────
= Setting it up

+ *Activate the module* for the tenant, with its licence.
+ *Choose where conversations are filed.* The module's setting _Where conversations are filed_#idx("repository
  setting") takes the id of a repository or folder. Group conversations and the shared folders of one-to-one chats are
  created there. Give it an administrator's rights only: each member gets access to their own conversations and
  nothing else, and the Messenger keeps those rights in step with each conversation's members.
+ *Let people enrol.* Members need an enrolled certificate before anyone can start a conversation with them.

```sh
echo <repository id> | saconsole module settings set messenger messenger.repositoryId --stdin
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

- *Membership is the current epoch's keys.* Whoever holds a key for the current epoch is a member; the folder's access
  list follows.
- *Removing someone starts a new epoch.* An administrator's device makes a new key and wraps it to the members who
  remain, so the removed member cannot read what follows.
- *Leaving does too — made by someone who stays.* The person leaving makes no key, because they would then know it.
  Until a remaining member's app publishes the next epoch, nothing more is sent; the app does that by itself before its
  next message.
- *A joining member starts at the current epoch.* They read from joining on, never the history before it.

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

== The "New messages" notification

When a message arrives, every other member gets a notification that says *"New messages"* — never the sender, never the
text, because it can reach a lock screen. It is *never mailed*. While it is unread, further messages fold into it
rather than adding more, so the phone is pushed once per burst; reading the conversation clears it, and the next
message pushes again.

// ─────────────────────────────────────────────────────────────────────────────
= Photos and files

A photo or file is sealed on the device like a message and uploaded straight to the archive's storage. It is filed
once, as a *Message Object*#idx("Message Object") in the conversation's folder, and each member finds it through their
own chat folder.

- *Only sealed content is accepted.* The archive checks that every Message Object is a sealed envelope and refuses
  anything else, and hands sealed files out on every encryption tier.
- *Kept apart from the archive.* Message Objects live in a storage area of their own and are left out of search,
  previews, text recognition, retention schedules and legal holds: the archive cannot read them, and their lifetime is
  the conversation's.
- *Delete for me* removes your reference; *delete for everyone* removes the file for all.

// ─────────────────────────────────────────────────────────────────────────────
= What the server can see

#table(
  columns: (1fr, 1fr),
  align: (left, left),
  table.header([*The server sees*], [*The server cannot see*]),
  [who is in which conversation, and who administers a group], [the text of any message],
  [who sends when, and how large each message and file is], [the content of any photo or file],
  [read markers, edits and deletions — that they happened], [any conversation key],
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

- *Backing up and restoring* a user's conversations, also onto another instance, is in preparation.
- *Disappearing messages* are not offered.

#index-page()
