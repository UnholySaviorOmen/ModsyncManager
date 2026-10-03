# Paywall Mods Policy

**Document version:** 1.0
**Updated:** 2026-10-03

ModsyncManager's policy regarding mods that require payment
(paywall mods) and their interaction with the `modlist.json`
format.

This document applies **only to the manifest format**. It does not
regulate how pack authors behave outside of ModsyncManager, does
not set rules for third-party platforms, and is not legal advice.

---

## 1. Why this document exists

`modlist.json` is an instruction. Each entry tells the installer:
"take the file from here and put it there." If an entry points to
a source whose access is protected by a paywall, the installer
will bypass that protection. This creates three problems:

1. **Technical.** Bypassing a paywall requires interfering with
   someone else's access control system. ModsyncManager is not a
   tool for such interference.
2. **Legal.** Circumventing technical access controls is an
   independent violation, regardless of whether anything is
   copied.
3. **Ethical.** Pack authors in the ModsyncManager space operate
   in an environment where respect for others' work is the norm.

This document records what may and may not be included in a
manifest, and why.

---

## 2. What counts as a paywall mod

A **paywall mod** is any mod whose files are available only after
payment (one-time, subscription, or a donation that grants
access). Forms include:

- **Patreon / Boosty / Ko-fi** — subscription or one-time payment
  for file access.
- **Discord roles** — paid access to channels containing files.
- **Verified Creations** (Bethesda) — third-party mods approved
  by Bethesda for sale through the in-game Creations store.
- **"Early access" / Beta** — files available only to paying
  users, later released publicly (time-limited paywall).
- **Any other form** where a file is available only after payment.

**Not a paywall mod:**

- **Creation Club (AECC).** Content commissioned and paid for by
  Bethesda as contract work. Considered official content on par
  with DLC.
- **DLC from the game's developer.** Official add-ons.
- **Donations without quid-pro-quo.** "Thank you" without an
  obligation to provide anything in return.
- **Free mods** whose author accepts donations. A donation is not
  a condition of access.

---

## 3. What may and may not go into `modlist.json`

| Category | In manifest | Reason |
|---|---|---|
| **Free mods** (Nexus, GitHub, mirrors) | ✅ Allowed | Author released them publicly. |
| **Creation Club (AECC)** | ✅ Allowed | Official Bethesda content, DLC-equivalent. |
| **DLC from the game's developer** | ✅ Allowed | Official content. |
| **Verified Creations** | ❌ Not allowed | Require purchase; Bethesda approved sale, not redistribution. |
| **Patreon / Boosty / Ko-fi mods** | ❌ Not allowed | Paywall; author did not permit free redistribution. |
| **Mods behind Discord paywall** | ❌ Not allowed | Same. |
| **Paid "early access" / Beta** | ❌ Not allowed | Same. |
| **Patches for paywall mods** | ❌ Not allowed | Facilitating distribution of content available only for payment. |
| **"Lite / Trial / Demo" versions** | ❌ Not allowed | Stripped-down versions promoting a purchase — a form of paywall. |
| **Dependencies on paywall mods** | ❌ Not allowed | If a mod cannot work without a purchase, the pack requires a paywall. |
| **Links that bypass paywall** | ❌ Not allowed | Circumventing technical protection is an independent violation. |

---

## 4. Why "the mod is illegal, so it's fine" does not work

A common argument: "A paywall mod violates the game's EULA, so
the author has no rights, so I can include it in the manifest for
free."

This is a misconception, for three reasons.

**4.1. Violating the EULA does not void copyright.**

A game's EULA is a contract between the mod author and the game's
developer. Violating that contract is a breach of contract, not a
loss of copyright. A mod author who sells their mod in violation
of the EULA remains the author. The game's developer may take
action against them, but this does not make the mod "ownerless."

**4.2. You are not a party to that contract.**

You are not part of the relationship "mod author ↔ game
developer." You gain no rights from the author's breach of their
obligations to the developer. Someone else's violation does not
grant you permission.

**4.3. Bypassing a paywall is an independent violation.**

Even if the mod were "ownerless," circumventing a technical
access control remains an independent offense. Opening access in
bypass of a paywall is a violation, regardless of whether
anything is copied.

---

## 5. Why "I paid for it, so I can redistribute it" does not work

The first sale doctrine applies **only to physical media**. It
does not apply to digital copies: uploading a file to the
internet is "communication to the public," an independent act to
which exhaustion does not extend.

You purchased a **license to use**, not ownership of the file.
The license gives you the right to play, but not the right to
distribute.

If the mod was sold through Verified Creations, the EULA
explicitly forbids free redistribution. If through Patreon, the
author certainly never granted permission.

**Conclusion:** purchasing does not turn you into a distributor.

---

## 6. What to do instead of a paywall mod

If a pack needs content available only for payment, there are
three clean paths.

**6.1. A separate guide outside the manifest.**

The pack author publishes — **separately** from `modlist.json` —
an instruction: "this mod is available on Patreon, buy it, then
install it on top of the pack." The manifest works without that
mod.

Wabbajack uses the same model: the list cannot require a
purchase, but the author may provide a separate instruction.

**6.2. Replacing with a free alternative.**

If a free mod solves the same problem, use it.

**6.3. Dropping the content.**

If the mod is critical and there is no free replacement, the pack
is built without it. If the pack is incomplete without it, then
the paywall mod is not "complementing" the pack — the pack
"depends" on the paywall. Such a pack is not allowed.

---

## 7. Creation Club: a special case

Creation Club (AECC) is not a paywall mod under this policy. It
is **official Bethesda content**, equivalent to DLC. Bethesda
commissioned it from authors as contract work and sells it
through the in-game store.

What this means for a manifest:

- **Creation Club may be included** in `modlist.json`.
- **A link to the Creation Club page on Steam / Bethesda.net is
  allowed** — same as a DLC link.
- **The pack author is not required** to make the pack work
  without Creation Club, if they explicitly state that it is part
  of the pack.

**Verified Creations are not the same.** These are third-party
mods that Bethesda merely approved for sale, not commissioned.
Nexus does not treat them as official content and applies the
same restrictions as to Patreon mods. ModsyncManager follows the
same logic.

---

## 8. Responsibility of the pack author

The pack author is responsible for compliance with this policy.
ModsyncManager is a **tool**. It does not check manifests for
paywall mods and is not an arbiter.

If a pack author includes a paywall mod in a manifest:

- **Technically**, the installer will try to download the file
  but will fail — there is no access.
- **Legally**, the pack author assumes the risks associated with
  bypassing a paywall.
- **Reputationally**, a pack that includes paywall mods
  contradicts the project's philosophy.

ModsyncManager does not relieve the pack author of this
responsibility.

---

## 9. Final policy — eight points

1. **Paywall mods are not included in a manifest.**
2. **Verified Creations are not included.**
3. **Creation Club is included** (official content, DLC-equivalent).
4. **Patches for paywall mods are not included.**
5. **Dependencies on paywall mods are not allowed.**
6. **"Lite / Demo / Trial" versions are not allowed.**
7. **Separate guides** for paid mods are allowed — outside the
   manifest.
8. **Selling a manifest** is allowed, but the manifest must not
   require paywall mods.

---

## 10. What this document does not regulate

- **How a pack author distributes their manifest.** Selling,
  donations, free — that is their choice. This document regulates
  only the contents of `modlist.json`.
- **What a pack author does outside ModsyncManager.** Separate
  guides, streams, tutorial videos — outside the scope.
- **Third-party platform rules.** Nexus, Wabbajack, and Steam
  Workshop have their own rules that apply when publishing **on
  those platforms**. This document does not replace them.
- **Legal advice.** This document explains the project's
  position; it does not provide legal counsel. A pack author
  uncertain about the legality of a specific action should
  consult a lawyer.

---

## 11. Sources

ModsyncManager's position is based on:

- The Skyrim Creation Kit EULA and other Bethesda game EULAs,
  regarding the prohibition of selling user-generated content.
- Article 1299 of the Civil Code of the Russian Federation,
  regarding the prohibition of circumventing technical
  protection measures.
- Wabbajack's public policy on paywall mods.
- Nexus Mods' public policy on Collections, patches, and
  dependencies on paid content.

This is not legal advice but an internal project policy.
