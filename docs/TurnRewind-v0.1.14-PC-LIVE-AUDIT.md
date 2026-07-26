# TurnRewind v0.1.14 PC live audit

Environment: Steam PC v0.107.1

## Mutation

- Snapshot roster: player + one Nibbit, total 2.
- Removed the original Nibbit.
- Spawned two replacement Nibbits.
- Mutated roster total: 3.
- Replaced player Strength 3 with Strength 9 and removed the enemy's Vulnerable.

## Restore result

- Roster changed from 3 back to 2.
- Original creature references and snapshot order were restored.
- Both post-snapshot creatures were removed and detached.
- Non-player visual nodes matched the restored model roster one-to-one.
- Strength: amount 3, amount-on-turn-start 2, duration flag true.
- Vulnerable: amount 2, amount-on-turn-start 1, duration flag true.

Result: PASS.
