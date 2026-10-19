import type { DepartmentReader } from '../ports/department-reader';

export class LoadDepartments {
  constructor(private readonly reader: DepartmentReader) {}

  execute(signal?: AbortSignal) {
    return this.reader.readAll(signal);
  }
}
