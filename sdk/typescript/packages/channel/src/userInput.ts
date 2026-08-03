export interface UserInputQuestionOption {
  label: string;
  description: string;
}

export interface UserInputQuestion {
  id: string;
  header: string;
  question: string;
  isOther: boolean;
  isSecret: boolean;
  options: UserInputQuestionOption[];
}

export interface UserInputAnswer {
  answers: string[];
}

export interface UserInputResponse extends Record<string, unknown> {
  answers: Record<string, UserInputAnswer>;
}

export interface UserInputPromptOptions {
  title?: string;
  requestId?: boolean;
}

export interface UserInputQuestionRequest {
  request: Record<string, unknown>;
  question: UserInputQuestion;
  questionIndex: number;
  questionCount: number;
}

export function emptyUserInputResponse(): UserInputResponse {
  return { answers: {} };
}

export function normalizeUserInputQuestions(request: Record<string, unknown>): UserInputQuestion[] {
  const rawQuestions = Array.isArray(request.questions) ? request.questions : [];
  return rawQuestions
    .filter((item): item is Record<string, unknown> => item != null && typeof item === "object" && !Array.isArray(item))
    .map((item, index) => {
      const id = String(item.id ?? `question_${index + 1}`).trim() || `question_${index + 1}`;
      const rawOptions = Array.isArray(item.options) ? item.options : [];
      const options = rawOptions
        .filter((option): option is Record<string, unknown> =>
          option != null && typeof option === "object" && !Array.isArray(option),
        )
        .map((option) => ({
          label: String(option.label ?? "").trim(),
          description: String(option.description ?? "").trim(),
        }))
        .filter((option) => option.label.length > 0);

      return {
        id,
        header: String(item.header ?? "").trim(),
        question: String(item.question ?? "").trim(),
        isOther: item.isOther !== false,
        isSecret: Boolean(item.isSecret),
        options,
      };
    });
}

export function canUseNativeSingleChoiceUserInput(request: Record<string, unknown>): boolean {
  const questions = normalizeUserInputQuestions(request);
  return questions.length === 1 && !questions[0]!.isSecret && questions[0]!.options.length > 0;
}

export function splitUserInputRequestByQuestion(request: Record<string, unknown>): UserInputQuestionRequest[] {
  const questions = normalizeUserInputQuestions(request);
  const requestId = String(request.requestId ?? "").trim();
  return questions.map((question, questionIndex) => {
    const stepRequestId = questions.length > 1 && requestId
      ? `${requestId}:${questionIndex + 1}`
      : requestId;
    return {
      request: {
        ...request,
        requestId: stepRequestId,
        questions: [question],
      },
      question,
      questionIndex,
      questionCount: questions.length,
    };
  });
}

export function mergeUserInputResponses(responses: UserInputResponse[]): UserInputResponse {
  const answers: Record<string, UserInputAnswer> = {};
  for (const response of responses) {
    const responseAnswers = asRecord(response.answers);
    for (const [questionId, answer] of Object.entries(responseAnswers)) {
      if (!answer || typeof answer !== "object" || Array.isArray(answer)) continue;
      const values = (answer as UserInputAnswer).answers;
      if (Array.isArray(values)) {
        answers[questionId] = { answers: values.map((value) => String(value)) };
      }
    }
  }
  return { answers };
}

export function hasUserInputAnswer(response: UserInputResponse, questionId: string): boolean {
  const answer = response.answers[questionId];
  return Array.isArray(answer?.answers) && answer.answers.length > 0;
}

export function userInputResponseForSingleChoice(
  request: Record<string, unknown>,
  optionIndex: number,
): UserInputResponse {
  const question = normalizeUserInputQuestions(request)[0];
  const option = question?.options[optionIndex];
  if (!question || !option) return emptyUserInputResponse();
  return {
    answers: {
      [question.id]: { answers: [option.label] },
    },
  };
}

export function userInputResponseFromText(
  request: Record<string, unknown>,
  replyText: string,
): UserInputResponse | null {
  const questions = normalizeUserInputQuestions(request);
  if (questions.length === 0) return emptyUserInputResponse();
  const text = replyText.trim();
  if (!text) return null;

  if (questions.length === 1) {
    const answer = parseAnswerForQuestion(questions[0]!, text);
    if (!answer) return null;
    return { answers: { [questions[0]!.id]: { answers: [answer] } } };
  }

  const answers: Record<string, UserInputAnswer> = {};
  for (const segment of splitMultiQuestionSegments(text)) {
    const parsed = segment.match(/^\s*(?:q)?(\d+)\s*[:：=.)、-]\s*(.+?)\s*$/i);
    if (!parsed) continue;
    const questionIndex = Number(parsed[1]) - 1;
    const question = questions[questionIndex];
    if (!question) continue;
    const answer = parseAnswerForQuestion(question, parsed[2] ?? "");
    if (answer) answers[question.id] = { answers: [answer] };
  }

  return Object.keys(answers).length > 0 ? { answers } : null;
}

export function buildUserInputPrompt(
  request: Record<string, unknown>,
  options: UserInputPromptOptions = {},
): string {
  const questions = normalizeUserInputQuestions(request);
  const title = options.title ?? "DotCraft needs your input";
  const lines: string[] = [title];
  const requestId = String(request.requestId ?? "").trim();
  if (options.requestId !== false && requestId) lines.push(`Request: ${requestId}`);
  lines.push("");

  if (questions.length === 0) {
    lines.push("No questions were provided.");
    return lines.join("\n").trim();
  }

  questions.forEach((question, questionIndex) => {
    const prefix = questions.length > 1 ? `${questionIndex + 1}. ` : "";
    const heading = question.header || `Question ${questionIndex + 1}`;
    lines.push(`${prefix}${heading}`);
    if (question.question) lines.push(question.question);
    if (question.isSecret) {
      lines.push("This chat cannot hide secret answers; reply only if it is safe to share here.");
    }
    question.options.forEach((option, optionIndex) => {
      const detail = option.description ? ` - ${option.description}` : "";
      lines.push(`${optionIndex + 1}) ${option.label}${detail}`);
    });
    if (question.isOther) {
      lines.push("0) Other / free text");
    }
    lines.push("");
  });

  if (questions.length === 1) {
    const question = questions[0]!;
    if (question.options.length > 0 && question.isOther) {
      lines.push("Reply with an option number, or reply `0 your answer` for other.");
    } else if (question.options.length > 0) {
      lines.push("Reply with an option number.");
    } else {
      lines.push("Reply with your answer.");
    }
  } else {
    lines.push("Reply one answer per line, for example:");
    lines.push("1: 2");
    lines.push("2: 0 custom answer");
  }

  return lines.join("\n").trim();
}

function splitMultiQuestionSegments(text: string): string[] {
  const lineSegments = text.split(/\r?\n/).map((line) => line.trim()).filter(Boolean);
  if (lineSegments.length > 1) return lineSegments;
  return text.split(/[;；]/).map((segment) => segment.trim()).filter(Boolean);
}

function parseAnswerForQuestion(question: UserInputQuestion, rawAnswer: string): string | null {
  const trimmed = rawAnswer.trim();
  if (!trimmed) return null;

  const numberMatch = trimmed.match(/^(?:选|option\s*)?(\d+)(?:[.)、\s:-]+(.*))?$/i);
  if (numberMatch) {
    const optionNumber = Number(numberMatch[1]);
    const remainder = String(numberMatch[2] ?? "").trim();
    if (optionNumber === 0) {
      return question.isOther && remainder ? remainder : null;
    }
    const option = question.options[optionNumber - 1];
    if (option) return option.label;
  }

  const exactOption = question.options.find((option) => option.label.toLowerCase() === trimmed.toLowerCase());
  if (exactOption) return exactOption.label;
  if (question.isOther || question.options.length === 0) return trimmed;
  return null;
}

function asRecord(value: unknown): Record<string, unknown> {
  return value != null && typeof value === "object" && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : {};
}
