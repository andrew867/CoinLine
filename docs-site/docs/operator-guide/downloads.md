# Downloads

Open the terminal's table assignment and check that it points to the intended published set. A matched terminal requesting tables through NCC creates a batch for that set. An operator can also create a batch from the terminal view or by calling `POST /api/terminals/{id}/downloads`.

Open Downloads and select the batch to see each record's phase and terminal acknowledgement. If an item fails, check its error detail and the associated NCC session before retrying. Cancel and retry require confirmation. See [Table distribution](../protocols/table-distribution.md).
