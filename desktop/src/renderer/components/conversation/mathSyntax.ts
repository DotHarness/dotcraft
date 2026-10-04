import { factorySpace } from 'micromark-factory-space'
import { markdownLineEnding } from 'micromark-util-character'
import type { Code, Construct, Extension, Resolver, State, Token, TokenizeContext, Tokenizer } from 'micromark-util-types'

declare module 'micromark-util-types' {
  interface TokenTypeMap {
    mathFlow: 'mathFlow'
    mathFlowFence: 'mathFlowFence'
    mathFlowFenceSequence: 'mathFlowFenceSequence'
    mathFlowValue: 'mathFlowValue'
    mathText: 'mathText'
    mathTextData: 'mathTextData'
    mathTextPadding: 'mathTextPadding'
    mathTextSequence: 'mathTextSequence'
  }
}

const SPACE = 32
const DOLLAR = 36
const LEFT_PARENTHESIS = 40
const RIGHT_PARENTHESIS = 41
const LEFT_SQUARE_BRACKET = 91
const BACKSLASH = 92
const RIGHT_SQUARE_BRACKET = 93

interface Delimiters {
  open: readonly number[]
  close: readonly number[]
}

const DOLLARS: Delimiters = { open: [DOLLAR, DOLLAR], close: [DOLLAR, DOLLAR] }
const BRACKETS: Delimiters = { open: [BACKSLASH, LEFT_SQUARE_BRACKET], close: [BACKSLASH, RIGHT_SQUARE_BRACKET] }
const PARENTHESES: Delimiters = { open: [BACKSLASH, LEFT_PARENTHESIS], close: [BACKSLASH, RIGHT_PARENTHESIS] }

/**
 * Math in `$$…$$`, `\[…\]` and `\(…\)`, emitted as the `mathFlow` and `mathText` tokens
 * `mdast-util-math` reads. A single `$` stays text so amounts are never math.
 */
export function mathSyntax(): Extension {
  return {
    flow: {
      [DOLLAR]: mathFlow(DOLLARS),
      [BACKSLASH]: mathFlow(BRACKETS)
    },
    text: {
      [DOLLAR]: mathText(DOLLARS),
      [BACKSLASH]: [mathText(PARENTHESES), mathText(BRACKETS)]
    }
  }
}

const nonLazyContinuation: Construct = { tokenize: tokenizeNonLazyContinuation, partial: true }

function mathFlow({ open, close }: Delimiters): Construct {
  const tokenize: Tokenizer = function (effects, ok, nok) {
    const self = this
    const tail = self.events[self.events.length - 1]
    const initialSize = tail && tail[1].type === 'linePrefix' ? tail[2].sliceSerialize(tail[1], true).length : 0
    let size = 0
    return start

    function start(code: Code): State | undefined {
      effects.enter('mathFlow')
      effects.enter('mathFlowFence')
      effects.enter('mathFlowFenceSequence')
      return sequenceOpen(code)
    }

    function sequenceOpen(code: Code): State | undefined {
      if (size < open.length) {
        if (code !== open[size]) return nok(code)
        effects.consume(code)
        size++
        return sequenceOpen
      }
      effects.exit('mathFlowFenceSequence')
      return factorySpace(effects, fenceEnd, 'whitespace')(code)
    }

    function fenceEnd(code: Code): State | undefined {
      if (code !== null && !markdownLineEnding(code)) return nok(code)
      effects.exit('mathFlowFence')
      if (self.interrupt) return ok(code)
      return effects.attempt(nonLazyContinuation, beforeNonLazyContinuation, after)(code)
    }

    function beforeNonLazyContinuation(code: Code): State | undefined {
      return effects.attempt({ tokenize: tokenizeClosingFence, partial: true }, after, contentStart)(code)
    }

    function contentStart(code: Code): State | undefined {
      return (initialSize ? factorySpace(effects, beforeContentChunk, 'linePrefix', initialSize + 1) : beforeContentChunk)(code)
    }

    function beforeContentChunk(code: Code): State | undefined {
      if (code === null) return after(code)
      if (markdownLineEnding(code)) return effects.attempt(nonLazyContinuation, beforeNonLazyContinuation, after)(code)
      effects.enter('mathFlowValue')
      return contentChunk(code)
    }

    function contentChunk(code: Code): State | undefined {
      if (code === null || markdownLineEnding(code)) {
        effects.exit('mathFlowValue')
        return beforeContentChunk(code)
      }
      effects.consume(code)
      return contentChunk
    }

    function after(code: Code): State | undefined {
      effects.exit('mathFlow')
      return ok(code)
    }

    function tokenizeClosingFence(this: TokenizeContext, effects: Parameters<Tokenizer>[0], ok: State, nok: State): State {
      let matched = 0
      return factorySpace(
        effects,
        beforeSequenceClose,
        'linePrefix',
        self.parser.constructs.disable.null?.includes('codeIndented') ? undefined : 4
      )

      function beforeSequenceClose(code: Code): State | undefined {
        effects.enter('mathFlowFence')
        effects.enter('mathFlowFenceSequence')
        return sequenceClose(code)
      }

      function sequenceClose(code: Code): State | undefined {
        if (matched < close.length) {
          if (code !== close[matched]) return nok(code)
          effects.consume(code)
          matched++
          return sequenceClose
        }
        effects.exit('mathFlowFenceSequence')
        return factorySpace(effects, afterSequenceClose, 'whitespace')(code)
      }

      function afterSequenceClose(code: Code): State | undefined {
        if (code !== null && !markdownLineEnding(code)) return nok(code)
        effects.exit('mathFlowFence')
        return ok(code)
      }
    }
  }

  return { name: 'mathFlow', tokenize, concrete: true }
}

function tokenizeNonLazyContinuation(this: TokenizeContext, effects: Parameters<Tokenizer>[0], ok: State, nok: State): State {
  const self = this
  return start

  function start(code: Code): State | undefined {
    if (code === null) return ok(code)
    effects.enter('lineEnding')
    effects.consume(code)
    effects.exit('lineEnding')
    return lineStart
  }

  function lineStart(code: Code): State | undefined {
    return self.parser.lazy[self.now().line] ? nok(code) : ok(code)
  }
}

function mathText({ open, close }: Delimiters): Construct {
  // `$$$` is neither an opening nor a closing `$$`.
  const runs = open[0] === DOLLAR

  const tokenize: Tokenizer = function (effects, ok, nok) {
    let size = 0
    let sequence: Token
    return start

    function start(code: Code): State | undefined {
      effects.enter('mathText')
      effects.enter('mathTextSequence')
      return sequenceOpen(code)
    }

    function sequenceOpen(code: Code): State | undefined {
      if (size < open.length) {
        if (code !== open[size]) return nok(code)
        effects.consume(code)
        size++
        return sequenceOpen
      }
      if (runs && code === DOLLAR) return nok(code)
      effects.exit('mathTextSequence')
      return between(code)
    }

    function between(code: Code): State | undefined {
      if (code === null) return nok(code)
      if (code === close[0]) {
        sequence = effects.enter('mathTextSequence')
        size = 0
        return sequenceClose(code)
      }
      if (code === SPACE) {
        effects.enter('space')
        effects.consume(code)
        effects.exit('space')
        return between
      }
      if (markdownLineEnding(code)) {
        effects.enter('lineEnding')
        effects.consume(code)
        effects.exit('lineEnding')
        return between
      }
      effects.enter('mathTextData')
      return data(code)
    }

    function data(code: Code): State | undefined {
      if (code === null || code === SPACE || code === close[0] || markdownLineEnding(code)) {
        effects.exit('mathTextData')
        return between(code)
      }
      effects.consume(code)
      return data
    }

    function sequenceClose(code: Code): State | undefined {
      if (size < close.length && code === close[size]) {
        effects.consume(code)
        size++
        return sequenceClose
      }
      if (size === close.length && !(runs && code === DOLLAR)) {
        effects.exit('mathTextSequence')
        effects.exit('mathText')
        return ok(code)
      }
      sequence.type = 'mathTextData'
      return data(code)
    }
  }

  function previous(this: TokenizeContext, code: Code): boolean {
    return code !== open[0] || this.events[this.events.length - 1][1].type === 'characterEscape'
  }

  return { name: 'mathText', tokenize, resolve: resolveMathText, previous }
}

// Drops one padding space on each side and merges the rest into data, as code spans do.
const resolveMathText: Resolver = (events) => {
  let tailExitIndex = events.length - 4
  let headEnterIndex = 3
  let index: number
  let enter: number | undefined

  if (
    (events[headEnterIndex][1].type === 'lineEnding' || events[headEnterIndex][1].type === 'space') &&
    (events[tailExitIndex][1].type === 'lineEnding' || events[tailExitIndex][1].type === 'space')
  ) {
    index = headEnterIndex
    while (++index < tailExitIndex) {
      if (events[index][1].type === 'mathTextData') {
        events[tailExitIndex][1].type = 'mathTextPadding'
        events[headEnterIndex][1].type = 'mathTextPadding'
        headEnterIndex += 2
        tailExitIndex -= 2
        break
      }
    }
  }

  index = headEnterIndex - 1
  tailExitIndex++
  while (++index <= tailExitIndex) {
    if (enter === undefined) {
      if (index !== tailExitIndex && events[index][1].type !== 'lineEnding') enter = index
    } else if (index === tailExitIndex || events[index][1].type === 'lineEnding') {
      events[enter][1].type = 'mathTextData'
      if (index !== enter + 2) {
        events[enter][1].end = events[index - 1][1].end
        events.splice(enter + 2, index - enter - 2)
        tailExitIndex -= index - enter - 2
        index = enter + 2
      }
      enter = undefined
    }
  }

  return events
}
