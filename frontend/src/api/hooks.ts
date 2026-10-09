import { useQuery } from '@tanstack/react-query'
import { get } from './client'
import type { AdminRoomType, HotelInfo } from './types'

export const useHotel = () =>
  useQuery({ queryKey: ['hotel'], queryFn: () => get<HotelInfo>('/api/public/hotel'), staleTime: 10 * 60_000 })

export const useAdminRoomTypes = () =>
  useQuery({ queryKey: ['admin', 'room-types'], queryFn: () => get<AdminRoomType[]>('/api/admin/room-types') })
