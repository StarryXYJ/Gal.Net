# Design

Use behavior-preserving substitutions only: cache immutable JSON serialization options and test expectations, use invariant numeric formatting for variable strings, select the memory-based stream write overload, and use the standard disposed-object helper. Do not narrow public return types or rename domain APIs solely to satisfy analyzers.
