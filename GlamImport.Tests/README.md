# GlamImport.Tests

Checks the paste parser against sample pages: `dotnet run --project GlamImport.Tests` from the solution folder.

Each `Samples/<name>.txt` is a copied page and `<name>.expected.txt` is what the parser should read from it.
- **Add a sample:** paste a page into a new `.txt` file in `Samples` (replace real glam, creator and character names with placeholders), run the tests once, and check the `.expected.txt` it writes.
- **After changing the parser on purpose:** run with `-- --update` and review the changed `.expected.txt` files.
