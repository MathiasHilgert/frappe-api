# Notifications: rotating keys

The Notifications module holds two secrets. Both live only in the environment and are the same for the Api and the Worker.

## Unsubscribe signing key

Unsubscribe links are signed (`Notifications:Unsubscribe`). A link never expires, so a rotation must keep verifying the old ones.

1. Put the new key in `FRAPPE_NOTIFICATIONS_UNSUBSCRIBE_KEY` (base64 of at least 32 bytes).
2. Set `Notifications__Unsubscribe__CurrentKeyId` to a new id (the first rotation uses `2`).
3. Move the old key to `Notifications__Unsubscribe__RetiredKeys__<old id>` (`compose.yaml` passes ids `1` and `2` through; add a line for more).

A retired key only verifies. A link signed with it still opts the recipient out. An opt-out the recipient made with an older link and one made
with a newer link name the same recipient key, so the second one stages nothing.

## Address hash key

Addresses are stored as keyed hashes (`Notifications:AddressHash`). A rotation adds a key under `Notifications__AddressHash__Keys__<id>` and moves
`Notifications__AddressHash__CurrentKeyId` to it; every earlier key stays configured for as long as rows hashed with it matter.

- Reads probe every active key, so an opt-out or a suppression made before the rotation still holds after it.
- New rows are written under the current key only; nothing rewrites the old rows.
- Delivery deduplication does not span a rotation. A delivery's identity includes its recipient key, and for an address that key carries the
  hash key id. A message redelivered after a rotation, to an address that was resolved before it, can send a second time. The window is the
  time a message can still be redelivered; rotate when no send is in flight.
- An unsubscribe link carries only the recipient key it was issued for, never the address, so it cannot be re-hashed under another key. Its
  opt-out is recorded under that key, which the preference gate finds because it probes every active key of the address.
