export type Institution = {
  sigle: string;
  legalName: string;
  letterheadLine2: string;
  letterheadLine3: string;
  letterheadLine4: string;
  city: string;
  country: string;
  defaultBranchName: string;
  defaultBranchCode: string;
  primaryCurrency: string;
  secondaryCurrency: string;
  displayTimeZone: string;
};

export type RoleDto = {
  name: string;
  displayNameFr: string;
  displayNameHt: string;
  displayNameEn: string;
  isReadOnly: boolean;
};

export type StaffUser = {
  id: string;
  username: string;
  fullName: string;
  email: string;
  branchId: string;
  isActive: boolean;
  mustChangePassword: boolean;
  roles: RoleDto[];
  mfaEnabled?: boolean;
};

export type UserDto = {
  id: string;
  username: string;
  fullName: string;
  email: string;
  branchId: string;
  mustChangePassword: boolean;
};

export type MfaSetup = {
  manualKey: string;
  otpauthUrl: string;
  qrPngDataUrl: string;
};

export type BranchDto = {
  id: string;
  code: string;
  name: string;
  city: string;
  isHeadquarters: boolean;
};

export type LoginResponse = {
  accessToken?: string | null;
  expiresAtUtc?: string | null;
  expiresAtPortAuPrince?: string | null;
  user?: UserDto | null;
  institution?: Institution | null;
  branch?: BranchDto | null;
  roles?: RoleDto[] | null;
  mfaRequired?: boolean;
  mfaSetupRequired?: boolean;
  mfaTicket?: string | null;
  mfaSetup?: MfaSetup | null;
};

export type MeResponse = {
  user: UserDto;
  institution: Institution;
  branch: BranchDto;
  roles: RoleDto[];
};

export type Session = {
  token: string;
  user: UserDto;
  institution: Institution;
  branch: BranchDto;
  roles: RoleDto[];
};
