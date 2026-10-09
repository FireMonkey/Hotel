import type { BookingSource, BookingStatus, ChannelType, HousekeepingStatus, MealPlan, PaymentStatus, RoomStatus } from '@/api/types'

// Central place for UI wording — swap or translate here.

export const bookingStatusLabel: Record<BookingStatus, string> = {
  Pending: 'Beklemede',
  Confirmed: 'Onaylı',
  CheckedIn: 'Konaklıyor',
  CheckedOut: 'Çıkış yaptı',
  Cancelled: 'İptal',
  NoShow: 'Gelmedi',
}

export const paymentStatusLabel: Record<PaymentStatus, string> = {
  Unpaid: 'Ödenmedi',
  PartiallyPaid: 'Kısmi ödeme',
  Paid: 'Ödendi',
  Refunded: 'İade edildi',
  PaidToChannel: 'Kanala ödendi',
}

export const sourceLabel: Record<BookingSource, string> = {
  Direct: 'Web sitesi',
  Admin: 'Resepsiyon',
  BookingCom: 'Booking.com',
  Expedia: 'Expedia',
  Airbnb: 'Airbnb',
  Agoda: 'Agoda',
  Hotelbeds: 'Hotelbeds',
  ChannelManager: 'Kanal yöneticisi',
  ICal: 'iCal',
}

export const mealPlanLabel: Record<MealPlan, string> = {
  RoomOnly: 'Sadece oda',
  BedAndBreakfast: 'Oda + kahvaltı',
  HalfBoard: 'Yarım pansiyon',
  FullBoard: 'Tam pansiyon',
  AllInclusive: 'Her şey dahil',
}

export const roomStatusLabel: Record<RoomStatus, string> = {
  Available: 'Satışta',
  OutOfOrder: 'Arızalı',
  Maintenance: 'Bakımda',
}

export const housekeepingLabel: Record<HousekeepingStatus, string> = {
  Clean: 'Temiz',
  Dirty: 'Kirli',
  Inspected: 'Kontrol edildi',
}

export const channelLabel: Record<ChannelType, string> = {
  BookingCom: 'Booking.com',
  Expedia: 'Expedia',
  Channex: 'Channex',
  ICal: 'iCal',
}
