// Namespaces that moved between nopCommerce versions. NOP_500 is defined by the .csproj for 5.00 builds.
#if NOP_500
global using Nop.Services.Helpers; // IWebHelper moved here from Nop.Core in 5.00
// linq2db 6 moved its async query methods (CountAsync(token), ToListAsync(token)) to LinqToDB.Async.
global using LinqToDB.Async;
#endif
