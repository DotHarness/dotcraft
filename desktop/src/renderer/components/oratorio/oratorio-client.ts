import type {
  ItemDetailResponse, ItemSummaryDto, ReviewFindingResolutionKind, SourceSyncJobDto,
  TaskListResponse
} from './oratorio-contracts'

type Body = Record<string, unknown>

async function request<T>(method: 'GET' | 'POST' | 'PUT' | 'PATCH', path: string, body?: Body): Promise<T> {
  const response = await window.api.oratorio.request<T>({ method, path, body })
  return response.data
}

export const oratorioClient = {
  listTasks(query = ''): Promise<TaskListResponse> {
    return request('GET', `/api/v1/tasks${query ? `?${query}` : ''}`)
  },
  task(taskId: string): Promise<ItemDetailResponse> {
    return request('GET', `/api/v1/tasks/${encodeURIComponent(taskId)}`)
  },
  createLocalTask(body: Body): Promise<{ item: ItemSummaryDto }> { return request('POST', '/api/v1/local-tasks', body) },
  reorder(body: Body): Promise<{ tasks: ItemSummaryDto[] }> { return request('POST', '/api/v1/tasks/reorder', body) },
  itemAction(itemId: string, action: string, body: Body = {}): Promise<ItemDetailResponse> {
    return request('POST', `/api/v1/items/id/${encodeURIComponent(itemId)}/${action}`, body)
  },
  addComment(itemId: string, body: string, roundNumber?: number): Promise<ItemDetailResponse> {
    return request('POST', `/api/v1/items/id/${encodeURIComponent(itemId)}/comments`, { body, roundNumber })
  },
  askAgent(itemId: string, body: string, roundNumber?: number, modelId?: string): Promise<ItemDetailResponse> {
    return request('POST', `/api/v1/items/id/${encodeURIComponent(itemId)}/discussion-turns`, { body, roundNumber, modelId })
  },
  syncSourceDetails(itemId: string): Promise<ItemDetailResponse> { return request('POST', `/api/v1/items/id/${encodeURIComponent(itemId)}/source-details/sync`) },
  retrySourceWrite(writeId: string): Promise<ItemDetailResponse> { return request('POST', `/api/v1/source-writes/${encodeURIComponent(writeId)}/retry`) },
  updateReviewDraft(draftId: string, body: Body): Promise<ItemDetailResponse> { return request('PATCH', `/api/v1/review-drafts/${encodeURIComponent(draftId)}`, body) },
  publishReviewDraft(draftId: string): Promise<ItemDetailResponse> { return request('POST', `/api/v1/review-drafts/${encodeURIComponent(draftId)}/publish`) },
  discardReviewDraft(draftId: string): Promise<ItemDetailResponse> { return request('POST', `/api/v1/review-drafts/${encodeURIComponent(draftId)}/discard`) },
  resolveFinding(draftId: string, commentId: string, resolutionKind: ReviewFindingResolutionKind, note?: string): Promise<ItemDetailResponse> {
    return request('POST', `/api/v1/review-drafts/${encodeURIComponent(draftId)}/comments/${encodeURIComponent(commentId)}/resolve`, { resolutionKind, note })
  },
  reopenFinding(draftId: string, commentId: string): Promise<ItemDetailResponse> { return request('POST', `/api/v1/review-drafts/${encodeURIComponent(draftId)}/comments/${encodeURIComponent(commentId)}/reopen`) },
  deliverImplementation(draftId: string): Promise<ItemDetailResponse> { return request('POST', `/api/v1/implementation-drafts/${encodeURIComponent(draftId)}/deliver`) },
  updateFollowUp(draftId: string, body: Body): Promise<ItemDetailResponse> { return request('PATCH', `/api/v1/follow-up-drafts/${encodeURIComponent(draftId)}`, body) },
  discardFollowUp(draftId: string): Promise<ItemDetailResponse> { return request('POST', `/api/v1/follow-up-drafts/${encodeURIComponent(draftId)}/discard`) },
  createTaskFromFollowUp(draftId: string): Promise<ItemDetailResponse> { return request('POST', `/api/v1/follow-up-drafts/${encodeURIComponent(draftId)}/create-local-task`) },
  sync(provider: string, mode: 'incremental' | 'full' = 'incremental', projects?: string[]): Promise<SourceSyncJobDto> {
    return request('POST', `/api/v1/sources/${encodeURIComponent(provider)}/sync-jobs`, { mode, projects })
  }
}
