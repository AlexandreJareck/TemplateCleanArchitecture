import { InjectionToken } from '@angular/core';
import type { MasterDataPort } from '@application/master-data/master-data';
import type { SessionPort } from '@application/session/session';

export const SESSION_PORT = new InjectionToken<SessionPort>('SESSION_PORT');
export const MASTER_DATA_PORT = new InjectionToken<MasterDataPort>('MASTER_DATA_PORT');
