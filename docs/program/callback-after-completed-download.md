# Callback after a completed data download

A phone can accept an immediate MT19 callback during ordinary clock synchronization, perform its separate Craft data download, acknowledge its end-of-data record, and return the callback as MT9 on the same NCC connection. The callback return is a distinct management dialogue, not a duplicate of the earlier table request.

The host preserves the completed data batch and permits one newly acknowledged, due callback return to claim its own pending work after that download has ended. It does not release the per-dialogue guard while an earlier transfer is still pending or an identity cutover owns the link. Repeated MT9 requests without a newly acknowledged callback do not start another download.

The regression uses an ordinary assigned data record and a separate prepared disabled MT94. The Craft dialogue delivers only its assigned data. On the same socket, a code-only MT9 delivers and completes the original cancellation batch, without redelivering the assigned data, creating a third batch or changing the phone's assignment. A second case verifies that an explicit assigned-data callback uses its own batch identity rather than reusing the already completed Craft batch.

This is a host dispatch and acknowledgement workflow correction. It does not establish an actual code-server handshake, an image transfer or a terminal upgrade.
