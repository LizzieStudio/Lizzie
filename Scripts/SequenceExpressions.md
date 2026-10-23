# Sequence Expressions

Sequence Expressions (SeqEx) is a domain specific language for specifying sequences of strings or integers. It's similar to the text you write to specify pages for printing, `1-3, 5, 7`, but may include text and character, `1, 3, Skip, J-L`. SeqEx turns a string like this into an array of strings: `["1", "3", "Skip", "J", "K", "L"]`.

The parser uses the signature `bool TryParseStrings(string input, out string[] output, int? count = null)` which returns true when it succeeded. Whether it succeeds or fails, however, it returns an array of any strings that it was able to find in the input string. Expressions are separated by commas, their strings are trimmed, and extra commas are ignored (`, 1 ,, B ,` resolves to `["1", "B"]`).

Input strings are trimmed, but they may still have spaces: `A, Skip Next, B` resolves to `["A", "Skip Next", "B"]`. Commas are interpreted as a separator, so if you want a comma, you must escape the string by surrounding it with quotes: `A, "Draw, Discard", B` resolves to `["A", "Draw, Discard", "B"]`. The parser will return partial results even if the input has an error. For example, if the input is `1,"B`, the parse would fail, but it would also return `["1"]` for the part of the input that it could understand.

There is another call for when you want to specify a sequence of numbers, `bool TryParseNumbers(string input, out int[] output, int? count = null)`. It follows the same rules but will parse the output into integers before returning and will error out if any element isn't an integer. For most applications, like selecting a card from a deck, the elements are treated as if they're 1-indexed.

The two standard features are plain strings, which can have spaces, `Skip Next`, and ranges, `1-3` or `A-F`. Duplicate numbers are allowed and input order is preserved, so `2-3, 1-2` with number parsing resolves to `[2, 3, 1, 2]`. You can also specify ranges that descend: `C-A` resolves to `["C", "B", "A"]`.

You can take a sequence s and repeat it N times with the suffix `*N`. For example:
* Repeating a number: `1*3` resolves to `[1, 1, 1]`
* Repeating a range: `1-2*2` resolves to `[1, 2, 1, 2]`
* Repeating a sequence: `(1, 3-4)*2` resolves to `[1, 3, 4, 1, 3, 4]`
* Repeating 0 times returns nothing: `2*0` resolves to `[]`

You can take every Nth item of a sequence s, starting at the first, using the suffix `/N`. For example:
* Take every 2nd integer from a range: `2-7/2` resolves to `[2, 4, 6]`
* Take every 3rd integer from a sequence: `(1, 3-6, 8-10)/3` resolves to `[1, 5, 9]`
* `/1` is a no-op: `(1,3,5)/1` resolves to `[1, 3, 5]`
* `/0` returns false for an error

All of these features work for string parsing as well.
* Repeating: `(A, B)*2` resolves to `["A", "B", "A", "B"]`
* Nth string: `A-F/2` resolves to `["A", "C", "E"]`

As with most languages, expressions can be arbitrarily nested with operations, so you can combine operations like:
* Repeat a range and take every 2nd number: `1-3*3/2` resolves to `[1, 3, 2, 1, 3]`.
* Take every 2nd number from a range and repeat it: `1-3/2*3` resolves to `[1, 3, 1, 3, 1, 3]`.

More precisely, numbers `5`, ranges `1-5`, and sequences, `(1,5)` are all single expressions. The postfix unary operators, `*N` and `/N` work on whatever expression they follow and also create expressions. This means `1,3*2` resolves to `[1, 3, 3]`. Both `*N` and `/N` do not allow for whitespace, so `* 5` and `/ 5` parsed with `TryParseNumbers` are either different operations or errors. With `TryParseStrings` they will likely become `"* 5"` and `"/ 5"` instead, so be aware that `TryParseStrings` will likely mask errors.

`TryParseStrings` and `TryParseNumbers` have an optional argument for adding a count, `TryParseNumbers(string input, out int[] output, int? count = null)`, which specifies how many items to get back if the sequence has something called an unbounded repetition. An unbounded repetition does not specify a count, like `1-3*`. Normally, this is not repeated (`*1`), but when you specify a count and the sequence would produce fewer integers than the count, the one unbounded repetition is repeated to cover the difference. For example, parsing `1, 3*, 5` with a count of 5 would produce `[1, 3, 3, 3, 5]`. Without an unbounded repetition, the count is ignored: 1-2 with a count of 5 is still [1, 2].

When a sequence would produce more integers than the count, the `*` sequence is instead trimmed to fit the sequence into the count. This makes `*` useful when you want to make a range shorter. For example, you can specify `1, 3-100*, 2` with no expectation to ever use a count greater than 100. To get an unbounded range, however, you can simply end a range in `*`: `1-*`. This will continue the range for as long as is necessary to fill out the count: `1, 3-*` with a count of 5 would resolve to `[1, 3, 4, 5, 6]`.

If the count is so low that even eliminating the unbounded repetition entirely isn't enough to stay within the count, the unbounded repetition is eliminated and the final sequence will still be long: `1, 3-5*, 7` with a count of 0, 1, or 2 would resolve to `[1, 7]`. If you provide no count, `3-*` will resolve to nothing `[]`. Unbounded ranges and repetitions must be the top-level expression; you cannot use `*N`, `/N`, or `()` on `1-*` or `2*`. Two unbounded expressions in the same input is an error.

Warning: There is an internal limit of 10,000 integers per expression. If any expression goes beyond 10,000 (`1-10001`) the parse will exit with an error and any prior results.