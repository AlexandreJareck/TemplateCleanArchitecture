import type { Result } from '../common/result';
import type { Department } from '../organization/department';

export interface DepartmentReader {
  readAll(signal?: AbortSignal): Promise<Result<readonly Department[]>>;
}
