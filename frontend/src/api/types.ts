// Mirrors the backend DTOs (Hotel.Application / Hotel.Api). Dates are ISO strings (yyyy-MM-dd).

export type BookingStatus = 'Pending' | 'Confirmed' | 'CheckedIn' | 'CheckedOut' | 'Cancelled' | 'NoShow'
export type PaymentStatus = 'Unpaid' | 'PartiallyPaid' | 'Paid' | 'Refunded' | 'PaidToChannel'
export type BookingSource =
  | 'Direct' | 'Admin' | 'BookingCom' | 'Expedia' | 'Airbnb' | 'Agoda' | 'Hotelbeds' | 'ChannelManager' | 'ICal'
export type MealPlan = 'RoomOnly' | 'BedAndBreakfast' | 'HalfBoard' | 'FullBoard' | 'AllInclusive'
export type RoomStatus = 'Available' | 'OutOfOrder' | 'Maintenance'
export type HousekeepingStatus = 'Clean' | 'Dirty' | 'Inspected'
export type ChannelType = 'BookingCom' | 'Expedia' | 'Channex' | 'ICal'

export interface HotelInfo {
  name: string
  tagline?: string
  description?: string
  address?: string
  city?: string
  country?: string
  phone?: string
  email?: string
  checkInTime: string
  checkOutTime: string
  currency: string
  latitude?: number
  longitude?: number
  imageUrls: string[]
  facilities: string[]
}

export interface PublicRoomType {
  id: string
  code: string
  name: string
  description?: string
  maxAdults: number
  maxChildren: number
  sizeSquareMeters: number
  fromPrice: number
  currency: string
  amenities: string[]
  imageUrls: string[]
}

export interface NightlyPrice { date: string; price: number }

export interface RateQuote {
  ratePlanId: string
  code: string
  name: string
  mealPlan: MealPlan
  isRefundable: boolean
  freeCancellationDays?: number | null
  totalPrice: number
  averageNightlyPrice: number
  currency: string
  nights: NightlyPrice[]
}

export interface RoomTypeAvailability {
  roomTypeId: string
  code: string
  name: string
  description?: string
  maxAdults: number
  maxChildren: number
  sizeSquareMeters: number
  amenities: string[]
  imageUrls: string[]
  availableRooms: number
  rates: RateQuote[]
}

export interface Guest {
  firstName: string
  lastName: string
  email: string
  phone?: string | null
  country?: string | null
}

export interface PublicBooking {
  reference: string
  roomTypeName: string
  ratePlanName?: string
  checkIn: string
  checkOut: string
  nights: number
  adults: number
  children: number
  guestName: string
  totalAmount: number
  currency: string
  status: BookingStatus
  canCancel: boolean
}

export interface Booking {
  id: string
  reference: string
  source: BookingSource
  externalReference?: string | null
  channelName?: string | null
  roomTypeId: string
  roomTypeName: string
  ratePlanId?: string | null
  ratePlanName?: string | null
  roomId?: string | null
  roomNumber?: string | null
  checkIn: string
  checkOut: string
  nights: number
  adults: number
  children: number
  guest: Guest
  specialRequests?: string | null
  internalNotes?: string | null
  totalAmount: number
  paidAmount: number
  commissionAmount?: number | null
  currency: string
  status: BookingStatus
  paymentStatus: PaymentStatus
  isOverbooked: boolean
  createdAt: string
  cancelledAt?: string | null
  cancellationReason?: string | null
}

export interface Paged<T> { items: T[]; page: number; pageSize: number; totalCount: number }

export interface Dashboard {
  date: string
  totalRooms: number
  arrivals: number
  departures: number
  inHouse: number
  pendingBookings: number
  overbookedBookings: number
  occupancy: { date: string; booked: number; total: number; percent: number }[]
  revenueBySource: { source: BookingSource; bookings: number; revenue: number; commission: number }[]
  channels: { id: string; name: string; isEnabled: boolean; lastPull?: string; lastPush?: string; lastError?: string }[]
}

export interface RatePlan {
  id: string
  code: string
  name: string
  mealPlan: MealPlan
  isRefundable: boolean
  freeCancellationDays?: number | null
  priceFactor: number
  isActive: boolean
}

export interface AdminRoomType {
  id: string
  code: string
  name: string
  description?: string
  maxAdults: number
  maxChildren: number
  sizeSquareMeters: number
  basePrice: number
  currency: string
  amenities: string[]
  imageUrls: string[]
  isActive: boolean
  sortOrder: number
  roomCount: number
  ratePlans: RatePlan[]
}

export interface AdminRoom {
  id: string
  number: string
  floor: number
  roomTypeId: string
  roomTypeName: string
  status: RoomStatus
  housekeeping: HousekeepingStatus
  notes?: string
  currentBooking?: { id: string; reference: string; guestName: string; checkOut: string } | null
}

export interface AriCalendar {
  from: string
  to: string
  roomTypes: {
    roomTypeId: string
    name: string
    currency: string
    inventory: { date: string; physicalRooms: number; sellable: number; booked: number; available: number; hasOverride: boolean }[]
    ratePlans: {
      ratePlanId: string
      name: string
      days: { date: string; price: number; minStay: number; stopSell: boolean; closedToArrival: boolean; closedToDeparture: boolean; isDefault: boolean }[]
    }[]
  }[]
}

export interface ChannelSettingField {
  key: string
  label: string
  isSecret: boolean
  required: boolean
  placeholder?: string
  help?: string
}

export interface ChannelProvider {
  type: ChannelType
  displayName: string
  description: string
  supportsAriPush: boolean
  supportsReservationPull: boolean
  requiresPartnerCertification: boolean
  fields: ChannelSettingField[]
  documentationUrl?: string
}

export interface ChannelMapping {
  roomTypeId: string
  ratePlanId?: string | null
  externalRoomId: string
  externalRateId?: string | null
  importUrl?: string | null
}

export interface Channel {
  id: string
  channel: ChannelType
  name: string
  isEnabled: boolean
  externalPropertyId?: string | null
  settings: Record<string, string>
  hasSecret: boolean
  commissionPercent: number
  rateMarkupPercent: number
  lastReservationPullAt?: string | null
  lastAriPushAt?: string | null
  lastError?: string | null
  mappings: ChannelMapping[]
  iCalExports?: { roomTypeId: string; url: string }[] | null
}

export interface ChannelSyncLog {
  id: string
  direction: 'Push' | 'Pull'
  operation: string
  success: boolean
  itemCount: number
  message?: string
  durationMs: number
  createdAt: string
}

export interface AdminUser { id: string; email: string; fullName?: string; roles: string[]; locked: boolean }

export interface Me { id: string; email: string; fullName?: string; roles: string[] }
