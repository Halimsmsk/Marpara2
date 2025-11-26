/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { Area } from './Area';
export type Block = {
    contentElementTypeKey: string;
    allowAtRoot: boolean;
    allowInAreas: boolean;
    groupKey: string;
    areas?: Array<Area> | null;
    areaGridColumns?: number | null;
    // Thumbnail ve UI bilgileri
    thumbnail?: string | null;
    backgroundColor?: string | null;
    iconColor?: string | null;
    label?: string | null;
    description?: string | null;
    icon?: string | null;
};

